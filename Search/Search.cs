using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Move;

namespace Search
{
    public unsafe class Search
    {
        // Constants
        public const int MAX_PLY = 128;
        public const int MAX_MOVES = 256;
        public const int INFINITY = 30000;
        public const int MATE_VALUE = 29000;
        public const int DRAW_VALUE = 0;

        // Search state
        private Position rootPosition;
        private readonly SearchInfo searchInfo;
        private readonly TranspositionTable tt;
        private readonly MoveOrdering moveOrdering;

        // Move generation buffers - pre-allocated per thread
        private readonly Move.Move[][] moveBuffers;
        private readonly ArrayPool<Move.Move> movePool;

        // Statistics
        public ulong NodesSearched { get; private set; }
        public int SelectiveDepth { get; private set; }

        // Principal Variation
        private readonly Move.Move[,] pvTable;
        private readonly int[] pvLength;

        // Killer moves and history heuristic
        private readonly Move.Move[,] killerMoves;
        private readonly int[,] historyTable;

        // Counter moves
        private readonly Move.Move[,] counterMoves;

        // Evaluation history for pruning decisions
        private readonly int[] staticEvalStack;

        // Time management
        private readonly TimeManager timeManager;
        private CancellationTokenSource? searchCancellation;

        public Search(int ttSizeMB = 128, int threadCount = 1)
        {
            searchInfo = new SearchInfo();
            tt = new TranspositionTable(ttSizeMB);
            moveOrdering = new MoveOrdering();
            timeManager = new TimeManager();

            pvTable = new Move.Move[MAX_PLY, MAX_PLY];
            pvLength = new int[MAX_PLY];
            killerMoves = new Move.Move[MAX_PLY, 2];
            historyTable = new int[64, 64];
            counterMoves = new Move.Move[64, 64];
            staticEvalStack = new int[MAX_PLY];

            // Pre-allocate move buffers for each ply
            moveBuffers = new Move.Move[MAX_PLY][];
            for (int i = 0; i < MAX_PLY; i++)
            {
                moveBuffers[i] = new Move.Move[MAX_MOVES];
            }

            movePool = ArrayPool<Move.Move>.Create(MAX_MOVES, MAX_PLY);
            rootPosition = new Position();
        }

        public SearchResult StartSearch(Position position, SearchLimits limits)
        {
            rootPosition = new Position(position);
            searchCancellation = new CancellationTokenSource();

            // Reset search state
            NodesSearched = 0;
            SelectiveDepth = 0;
            Array.Clear(killerMoves, 0, killerMoves.Length);
            Array.Clear(historyTable, 0, historyTable.Length);
            Array.Clear(counterMoves, 0, counterMoves.Length);
            Array.Clear(staticEvalStack, 0, staticEvalStack.Length);

            // New search in TT
            tt.NewSearch();

            // Set time limits
            timeManager.StartSearch(limits, position.Turn);

            // Start iterative deepening
            return IterativeDeepening(limits);
        }

        private SearchResult IterativeDeepening(SearchLimits limits)
        {
            var bestMove = new Move.Move();
            var bestScore = -INFINITY;
            var searchResult = new SearchResult();

            // Generate root moves
            var rootMoves = GenerateRootMoves();
            if (rootMoves.Count == 0)
                return new SearchResult { BestMove = bestMove, Score = -MATE_VALUE };

            // Single legal move - return immediately
            if (rootMoves.Count == 1)
                return new SearchResult { BestMove = rootMoves[0].Move, Score = 0 };

            // Iterative deepening loop
            for (int depth = 1; depth <= limits.Depth && !ShouldStopSearch(); depth++)
            {
                var aspWindow = depth >= 4 ? 25 : INFINITY; // Start with wider window
                var alpha = -INFINITY;
                var beta = INFINITY;

                // Use aspiration window for deeper searches
                if (depth >= 4 && Math.Abs(bestScore) < MATE_VALUE - 100)
                {
                    alpha = bestScore - aspWindow;
                    beta = bestScore + aspWindow;
                }

                // Search with aspiration window
                int failHighCount = 0;
                int failLowCount = 0;
                var searchStartTime = timeManager.ElapsedMs();

                while (true)
                {
                    bestScore = SearchRoot(depth, alpha, beta, rootMoves);

                    // Check aspiration window failure
                    if (bestScore <= alpha)
                    {
                        // Fail low - widen window down
                        beta = (alpha + beta) / 2;
                        alpha = Math.Max(-INFINITY, alpha - aspWindow);
                        aspWindow *= 2; // Double the window
                        failLowCount++;

                        Console.WriteLine($"info string fail low at depth {depth}, widening window");
                    }
                    else if (bestScore >= beta)
                    {
                        // Fail high - widen window up
                        alpha = (alpha + beta) / 2;
                        beta = Math.Min(INFINITY, beta + aspWindow);
                        aspWindow *= 2;
                        failHighCount++;

                        Console.WriteLine($"info string fail high at depth {depth}, widening window");
                    }
                    else
                    {
                        // Search completed successfully
                        break;
                    }

                    // If too many failures or taking too long, use full window
                    if (failHighCount + failLowCount >= 3 ||
                        timeManager.ElapsedMs() - searchStartTime > limits.MoveTime / 4)
                    {
                        alpha = -INFINITY;
                        beta = INFINITY;
                        Console.WriteLine($"info string using full window at depth {depth}");

                        // Do one more search with full window
                        bestScore = SearchRoot(depth, alpha, beta, rootMoves);
                        break;
                    }

                    if (ShouldStopSearch())
                        break;
                }

                if (!ShouldStopSearch())
                {
                    bestMove = rootMoves[0].Move;
                    searchResult = new SearchResult
                    {
                        BestMove = bestMove,
                        Score = bestScore,
                        Depth = depth,
                        Nodes = NodesSearched,
                        Time = timeManager.ElapsedMs(),
                        Pv = GetPV()
                    };

                    // Print search info
                    PrintSearchInfo(searchResult);

                    // Early exit if mate found or if we found a forced mate
                    if (Math.Abs(bestScore) >= MATE_VALUE - 100)
                    {
                        Console.WriteLine($"info string mate found, stopping search");
                        break;
                    }

                    // Stop if we're running out of time and have a decent result
                    if (depth >= 6 && timeManager.ShouldStop())
                    {
                        Console.WriteLine($"info string time up, stopping search");
                        break;
                    }
                }
                else
                {
                    Console.WriteLine($"info string search stopped at depth {depth}");
                    break;
                }
            }

            return searchResult;
        }

        private int SearchRoot(int depth, int alpha, int beta, List<RootMove> rootMoves)
        {
            var bestScore = -INFINITY;
            pvLength[0] = 0;

            // Sort root moves by previous iteration scores
            rootMoves.Sort((a, b) => b.PreviousScore.CompareTo(a.PreviousScore));

            int bestMoveIndex = -1;

            for (int moveIndex = 0; moveIndex < rootMoves.Count; moveIndex++)
            {
                var rootMove = rootMoves[moveIndex];
                NodesSearched++;

                // Make move
                rootPosition.Play(rootPosition.Turn, rootMove.Move);

                int score;
                if (moveIndex == 0)
                {
                    // Full window search for first move
                    score = -AlphaBeta(depth - 1, -beta, -alpha, 1, true);
                }
                else
                {
                    // Late move reduction
                    int reduction = 0;
                    if (depth >= 3 && !rootMove.Move.IsCapture && !rootPosition.InCheck(rootPosition.Turn))
                    {
                        reduction = 1;
                        if (moveIndex > 5)
                            reduction = 2;
                        if (moveIndex > 12)
                            reduction = 3;
                    }

                    // Null window search
                    score = -AlphaBeta(depth - 1 - reduction, -alpha - 1, -alpha, 1, false);

                    // Re-search if needed
                    if (score > alpha && (score < beta || reduction > 0))
                        score = -AlphaBeta(depth - 1, -beta, -alpha, 1, false);
                }

                // Unmake move
                rootPosition.Undo(rootPosition.Turn.Flip(), rootMove.Move);

                rootMove.Score = score;
                rootMove.PreviousScore = rootMove.Score;

                if (score > bestScore)
                {
                    bestScore = score;

                    if (score > alpha)
                    {
                        alpha = score;
                        bestMoveIndex = moveIndex;

                        // Update PV
                        UpdatePV(rootMove.Move, 0);

                        if (score >= beta)
                            break;
                    }
                }

                if (ShouldStopSearch())
                    break;
            }

            // Move best move to front after the search loop
            if (bestMoveIndex > 0)
            {
                var bestMove = rootMoves[bestMoveIndex];
                rootMoves.RemoveAt(bestMoveIndex);
                rootMoves.Insert(0, bestMove);
            }

            return bestScore;
        }

        private int AlphaBeta(int depth, int alpha, int beta, int ply, bool pvNode)
        {
            // Check time and stop conditions
            if ((NodesSearched & 2047) == 0 && ShouldStopSearch())
                return 0;

            pvLength[ply] = ply;

            // Update selective depth
            if (ply > SelectiveDepth)
                SelectiveDepth = ply;

            // Terminal node checks
            if (ply >= MAX_PLY)
                return Evaluate();

            // Check for draw by repetition or 50-move rule
            if (ply > 0 && IsDrawByRepetition())
                return DRAW_VALUE;

            // Mate distance pruning
            int matingValue = MATE_VALUE - ply;
            if (matingValue < beta)
            {
                beta = matingValue;
                if (alpha >= matingValue)
                    return matingValue;
            }

            matingValue = -MATE_VALUE + ply;
            if (matingValue > alpha)
            {
                alpha = matingValue;
                if (beta <= matingValue)
                    return matingValue;
            }

            // Transposition table probe
            var ttEntry = tt.Probe(rootPosition.GetHash());
            var ttMove = ttEntry?.Move ?? new Move.Move();
            var ttHit = ttEntry.HasValue;

            if (ttHit && ttEntry.Value.Depth >= depth && !pvNode)
            {
                var entry = ttEntry.Value;
                var ttScore = ScoreFromTT(entry.Score, ply);

                if (entry.Flag == TTFlag.Exact ||
                    (entry.Flag == TTFlag.LowerBound && ttScore >= beta) ||
                    (entry.Flag == TTFlag.UpperBound && ttScore <= alpha))
                {
                    // Update killers/history from TT
                    if (ttScore >= beta && !ttMove.IsCapture)
                    {
                        UpdateKillers(ttMove, ply);
                        UpdateHistory(ttMove, depth);
                    }
                    return ttScore;
                }
            }

            // Quiescence search at leaf nodes
            if (depth <= 0)
                return Quiescence(alpha, beta, ply);

            // Static evaluation
            var inCheck = rootPosition.InCheck(rootPosition.Turn);
            int staticEval;

            if (inCheck)
            {
                staticEval = -INFINITY;
                staticEvalStack[ply] = staticEval;
            }
            else if (ttHit)
            {
                // Use TT score as better approximation
                staticEval = ttEntry.Value.Score;
                staticEvalStack[ply] = staticEval;
            }
            else
            {
                staticEval = Evaluate();
                staticEvalStack[ply] = staticEval;
            }

            // Improving - are we better than 2 plies ago?
            bool improving = ply >= 2 && !inCheck &&
                           staticEval > staticEvalStack[ply - 2];

            // Razoring
            if (!pvNode && !inCheck && depth <= 3 && staticEval + 300 * depth < alpha)
            {
                int razorScore = Quiescence(alpha, beta, ply);
                if (razorScore <= alpha)
                    return razorScore;
            }

            // Futility pruning
            if (!pvNode && !inCheck && depth <= 8 &&
                staticEval - 75 * depth >= beta &&
                staticEval < MATE_VALUE - MAX_PLY)
            {
                return staticEval;
            }

            // Null move pruning
            if (!pvNode && !inCheck && depth >= 3 && staticEval >= beta &&
                ply > 0 && HasNonPawnMaterial())
            {
                int R = 3 + depth / 6 + Math.Min(3, (staticEval - beta) / 200);
                R = Math.Min(depth - 1, R);

                // For null move, we need to create a special null move
                // Since we can't make an actual null move with the current Position class,
                // we'll skip null move pruning for now
                // TODO: Add null move support to Position class


                rootPosition.MakeNullMove();
                int nullScore = -AlphaBeta(depth - R - 1, -beta, -beta + 1, ply + 1, false);
                rootPosition.UnmakeNullMove();

                if (nullScore >= beta)
                {
                    // Don't return mate scores from null move
                    if (nullScore >= MATE_VALUE - MAX_PLY)
                        nullScore = beta;
                    
                    return nullScore;
                }
            }

            // Internal iterative deepening
            if (pvNode && depth >= 6 && ttMove.From == ttMove.To)
            {
                AlphaBeta(depth - 2, alpha, beta, ply, true);
                ttEntry = tt.Probe(rootPosition.GetHash());
                if (ttEntry.HasValue)
                    ttMove = ttEntry.Value.Move;
            }

            // Generate moves using pre-allocated buffer
            var moves = moveBuffers[ply];
            var moveCount = GenerateMovesInto(moves);

            if (moveCount == 0)
                return inCheck ? -MATE_VALUE + ply : DRAW_VALUE;

            // Order moves
            var prevMove = ply > 0 ? pvTable[0, ply - 1] : new Move.Move();
            moveCount = OrderMovesInPlace(moves, moveCount, ttMove, ply, prevMove);

            var bestScore = -INFINITY;
            var bestMove = new Move.Move();
            var movesSearched = 0;
            var quietMovesSeen = 0;

            for (int i = 0; i < moveCount; i++)
            {
                var move = moves[i];
                NodesSearched++;

                // Late move pruning
                if (!pvNode && !inCheck && quietMovesSeen > 0 && depth <= 8)
                {
                    int lmpThreshold = 3 + depth * depth;
                    if (!improving) lmpThreshold /= 2;

                    if (quietMovesSeen >= lmpThreshold)
                        continue;
                }

                // Make move
                rootPosition.Play(rootPosition.Turn, move);

                // Check if move is legal (gives check)
                bool givesCheck = rootPosition.InCheck(rootPosition.Turn);

                int score;

                // Principal variation search
                if (movesSearched == 0)
                {
                    score = -AlphaBeta(depth - 1, -beta, -alpha, ply + 1, pvNode);
                }
                else
                {
                    // Late move reduction
                    int reduction = 0;
                    if (depth >= 3 && movesSearched >= 3 && !move.IsCapture &&
                        !inCheck && !givesCheck && !pvNode)
                    {
                        reduction = LogarithmicReduction(depth, movesSearched);

                        // Reduce less for killers and winning captures
                        if (move == killerMoves[ply, 0] || move == killerMoves[ply, 1])
                            reduction--;

                        // Increase reduction for non-improving positions
                        if (!improving)
                            reduction++;

                        reduction = Math.Max(0, Math.Min(depth - 2, reduction));
                    }

                    // Null window search
                    score = -AlphaBeta(depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, false);

                    // Re-search if needed
                    if (score > alpha && (score < beta || reduction > 0))
                        score = -AlphaBeta(depth - 1, -beta, -alpha, ply + 1, pvNode);
                }

                // Unmake move
                rootPosition.Undo(rootPosition.Turn.Flip(), move);

                if (!move.IsCapture && move.Flags != MoveFlags.PrQueen && move.Flags != MoveFlags.PcQueen)
                    quietMovesSeen++;

                movesSearched++;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;

                    if (score > alpha)
                    {
                        alpha = score;
                        UpdatePV(move, ply);

                        if (score >= beta)
                        {
                            // Update killers and history for beta cutoffs
                            if (!move.IsCapture)
                            {
                                UpdateKillers(move, ply);
                                UpdateHistory(move, depth);
                                UpdateCounterMove(prevMove, move);
                            }

                            break;
                        }
                    }
                }

                if (ShouldStopSearch())
                    break;
            }

            // Store in transposition table
            var flag = bestScore >= beta ? TTFlag.LowerBound :
                      bestScore <= alpha ? TTFlag.UpperBound : TTFlag.Exact;

            tt.Store(rootPosition.GetHash(), depth, ScoreToTT(bestScore, ply), flag, bestMove);

            return bestScore;
        }

        private int Quiescence(int alpha, int beta, int ply)
        {
            NodesSearched++;

            if (ply >= MAX_PLY)
                return Evaluate();

            var inCheck = rootPosition.InCheck(rootPosition.Turn);
            var standPat = inCheck ? -INFINITY : Evaluate();

            if (!inCheck)
            {
                if (standPat >= beta)
                    return beta;

                // Big delta pruning
                const int BIG_DELTA = 900; // Queen value
                if (standPat < alpha - BIG_DELTA)
                    return alpha;

                if (standPat > alpha)
                    alpha = standPat;
            }

            // Generate captures using pre-allocated buffer
            var moves = moveBuffers[ply];
            var moveCount = GenerateCapturesInto(moves, inCheck);

            if (inCheck && moveCount == 0)
                return -MATE_VALUE + ply;

            // Order captures
            moveCount = OrderCapturesInPlace(moves, moveCount, ply);

            for (int i = 0; i < moveCount; i++)
            {
                var move = moves[i];

                // Delta pruning with promotion consideration
                if (!inCheck)
                {
                    int materialGain = GetPieceValue(rootPosition.At(move.To));

                    // Add promotion value
                    if ((move.Flags & MoveFlags.Promotions) != 0)
                    {
                        materialGain += GetPromotionValue(move.Flags) - 100; // Pawn value
                    }

                    if (standPat + materialGain + 200 < alpha)
                        continue;
                }

                rootPosition.Play(rootPosition.Turn, move);
                var score = -Quiescence(-beta, -alpha, ply + 1);
                rootPosition.Undo(rootPosition.Turn.Flip(), move);

                if (score > alpha)
                {
                    alpha = score;
                    if (score >= beta)
                        return beta;
                }
            }

            return alpha;
        }

        // Helper methods
        private int LogarithmicReduction(int depth, int moveNumber)
        {
            return (int)(0.5 + Math.Log(depth) * Math.Log(moveNumber) / 2.0);
        }

        private void UpdateKillers(Move.Move move, int ply)
        {
            if (move != killerMoves[ply, 0])
            {
                killerMoves[ply, 1] = killerMoves[ply, 0];
                killerMoves[ply, 0] = move;
            }
        }

        private void UpdateHistory(Move.Move move, int depth)
        {
            historyTable[(int)move.From, (int)move.To] += depth * depth;

            // Prevent overflow
            if (historyTable[(int)move.From, (int)move.To] >= 2000)
            {
                // Age history table
                for (int i = 0; i < 64; i++)
                    for (int j = 0; j < 64; j++)
                        historyTable[i, j] /= 2;
            }
        }

        private void UpdateCounterMove(Move.Move prevMove, Move.Move move)
        {
            if (prevMove.From != prevMove.To)
                counterMoves[(int)prevMove.From, (int)prevMove.To] = move;
        }

        private bool HasNonPawnMaterial()
        {
            var color = rootPosition.Turn;
            return rootPosition.BitboardOf(color, PieceType.Knight) != 0 ||
                   rootPosition.BitboardOf(color, PieceType.Bishop) != 0 ||
                   rootPosition.BitboardOf(color, PieceType.Rook) != 0 ||
                   rootPosition.BitboardOf(color, PieceType.Queen) != 0;
        }

        private bool IsFiftyMoveRule()
        {
            // The fifty-move rule should check halfmove clock
            // For now, return false as the Position class doesn't track halfmove clock
            // This would need to be implemented in the Position class
            return false;
        }

        private int GetPromotionValue(MoveFlags flags)
        {
            return flags switch
            {
                MoveFlags.PrQueen or MoveFlags.PcQueen => 900,
                MoveFlags.PrRook or MoveFlags.PcRook => 500,
                MoveFlags.PrBishop or MoveFlags.PcBishop => 330,
                MoveFlags.PrKnight or MoveFlags.PcKnight => 320,
                _ => 0
            };
        }

        // Move generation helpers - now using pre-allocated arrays
        private int GenerateMovesInto(Move.Move[] moveBuffer)
        {
            fixed (Move.Move* movesPtr = moveBuffer)
            {
                if (rootPosition.Turn == Color.White)
                    return rootPosition.GenerateLegalsInto<White>(movesPtr);
                else
                    return rootPosition.GenerateLegalsInto<Black>(movesPtr);
            }
        }

        private int GenerateCapturesInto(Move.Move[] moveBuffer, bool inCheck)
        {
            if (inCheck)
                return GenerateMovesInto(moveBuffer);

            var count = GenerateMovesInto(moveBuffer);

            // Filter captures and promotions in-place
            int captureCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (moveBuffer[i].IsCapture || (moveBuffer[i].Flags & MoveFlags.Promotions) != 0)
                {
                    if (captureCount != i)
                        moveBuffer[captureCount] = moveBuffer[i];
                    captureCount++;
                }
            }

            return captureCount;
        }

        private List<RootMove> GenerateRootMoves()
        {
            var buffer = new Move.Move[MAX_MOVES];
            var count = GenerateMovesInto(buffer);
            var rootMoves = new List<RootMove>(count);

            for (int i = 0; i < count; i++)
                rootMoves.Add(new RootMove { Move = buffer[i] });

            return rootMoves;
        }

        // Move ordering - in-place
        private int OrderMovesInPlace(Move.Move[] moves, int moveCount, Move.Move ttMove, int ply, Move.Move prevMove = default)
        {
            // Get counter move
            var counterMove = prevMove.From != prevMove.To ?
                counterMoves[(int)prevMove.From, (int)prevMove.To] : new Move.Move();

            return moveOrdering.OrderMoves(moves, moveCount, ttMove, killerMoves[ply, 0],
                                         killerMoves[ply, 1], historyTable, rootPosition, counterMove);
        }

        private int OrderCapturesInPlace(Move.Move[] moves, int moveCount, int ply)
        {
            return moveOrdering.OrderCaptures(moves, moveCount, rootPosition);
        }

        // PV management
        private void UpdatePV(Move.Move move, int ply)
        {
            pvTable[ply, ply] = move;

            for (int i = ply + 1; i < pvLength[ply + 1]; i++)
                pvTable[ply, i] = pvTable[ply + 1, i];

            pvLength[ply] = pvLength[ply + 1];
        }

        private Move.Move[] GetPV()
        {
            var pv = new Move.Move[pvLength[0]];
            for (int i = 0; i < pvLength[0]; i++)
                pv[i] = pvTable[0, i];
            return pv;
        }

        // Evaluation (simplified - should be in separate evaluator)
        private int Evaluate()
        {
            var score = 0;

            // Material count
            score += BitboardUtils.PopCount(rootPosition.BitboardOf(Color.White, PieceType.Pawn)) * 100;
            score += BitboardUtils.PopCount(rootPosition.BitboardOf(Color.White, PieceType.Knight)) * 320;
            score += BitboardUtils.PopCount(rootPosition.BitboardOf(Color.White, PieceType.Bishop)) * 330;
            score += BitboardUtils.PopCount(rootPosition.BitboardOf(Color.White, PieceType.Rook)) * 500;
            score += BitboardUtils.PopCount(rootPosition.BitboardOf(Color.White, PieceType.Queen)) * 900;

            score -= BitboardUtils.PopCount(rootPosition.BitboardOf(Color.Black, PieceType.Pawn)) * 100;
            score -= BitboardUtils.PopCount(rootPosition.BitboardOf(Color.Black, PieceType.Knight)) * 320;
            score -= BitboardUtils.PopCount(rootPosition.BitboardOf(Color.Black, PieceType.Bishop)) * 330;
            score -= BitboardUtils.PopCount(rootPosition.BitboardOf(Color.Black, PieceType.Rook)) * 500;
            score -= BitboardUtils.PopCount(rootPosition.BitboardOf(Color.Black, PieceType.Queen)) * 900;

            return rootPosition.Turn == Color.White ? score : -score;
        }

        private int GetPieceValue(Piece piece)
        {
            return Types.TypeOf(piece) switch
            {
                PieceType.Pawn => 100,
                PieceType.Knight => 320,
                PieceType.Bishop => 330,
                PieceType.Rook => 500,
                PieceType.Queen => 900,
                _ => 0
            };
        }

        // Repetition detection
        private bool IsDrawByRepetition()
        {
            return rootPosition.IsRepetition() || rootPosition.IsFiftyMoveRule();
        }

        // TT score adjustment
        private int ScoreFromTT(int score, int ply)
        {
            if (score >= MATE_VALUE - MAX_PLY)
                return score - ply;
            if (score <= -MATE_VALUE + MAX_PLY)
                return score + ply;
            return score;
        }

        private int ScoreToTT(int score, int ply)
        {
            if (score >= MATE_VALUE - MAX_PLY)
                return score + ply;
            if (score <= -MATE_VALUE + MAX_PLY)
                return score - ply;
            return score;
        }

        // Search control
        private bool ShouldStopSearch()
        {
            return searchCancellation?.IsCancellationRequested ?? false ||
                   timeManager.ShouldStop();
        }

        public void StopSearch()
        {
            searchCancellation?.Cancel();
        }

        private void PrintSearchInfo(SearchResult result)
        {
            Console.WriteLine($"info depth {result.Depth} seldepth {SelectiveDepth} score cp {result.Score} " +
                            $"nodes {result.Nodes} nps {result.Nodes * 1000 / (ulong)Math.Max(1, result.Time)} " +
                            $"time {result.Time} hashfull {tt.HashFull()} pv {string.Join(" ", result.Pv)}");
        }
    }

    // Helper classes with minimal modifications
    public class RootMove
    {
        public Move.Move Move { get; set; }
        public int Score { get; set; }
        public int PreviousScore { get; set; }
    }

    public class SearchResult
    {
        public Move.Move BestMove { get; set; }
        public int Score { get; set; }
        public int Depth { get; set; }
        public ulong Nodes { get; set; }
        public long Time { get; set; }
        public Move.Move[] Pv { get; set; } = Array.Empty<Move.Move>();
    }

    public class SearchLimits
    {
        public int Depth { get; set; } = 128;
        public long Time { get; set; } = long.MaxValue;
        public long Inc { get; set; }
        public int MovesToGo { get; set; }
        public bool Infinite { get; set; }
        public long MoveTime { get; set; }
    }

    public class SearchInfo
    {
        public bool Stopped { get; set; }
        public long StartTime { get; set; }
        public long StopTime { get; set; }
    }

    // Bitboard utilities
    public static class BitboardUtils
    {
        public static int PopCount(ulong b)
        {
            return Bitboard.PopCount(b);
        }
    }
}