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
        public const int QSEARCH_DEPTH = -1;  // Special depth marker for quiescence

        // Search state
        private Position rootPosition;
        private readonly SearchInfo searchInfo;
        private readonly TranspositionTable tt;
        private readonly MoveOrdering moveOrdering;

        // Move generation buffers - pre-allocated per thread
        private readonly Move.Move[][] moveBuffers;
        private readonly ArrayPool<Move.Move> movePool;

        private readonly object searchLock = new object();

        // Statistics
        public ulong NodesSearched { get; private set; }
        public int SelectiveDepth { get; private set; }
        public ulong QNodes { get; private set; }  // Quiescence nodes

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
        private volatile bool stopSearch = false;

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
            // Reset stop flag
            stopSearch = false;

            rootPosition = new Position(position);

            // Reset search state
            NodesSearched = 0;
            QNodes = 0;
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
                var initialAspWindow = depth >= 4 ? 50 : INFINITY;
                var alpha = -INFINITY;
                var beta = INFINITY;

                // Use aspiration window for deeper searches
                if (depth >= 4 && Math.Abs(bestScore) < MATE_VALUE - 100)
                {
                    alpha = bestScore - initialAspWindow;
                    beta = bestScore + initialAspWindow;
                }

                // Search with aspiration window
                int failCount = 0;
                const int MAX_ASPIRATION_RETRIES = 5;
                var aspWindow = initialAspWindow;
                var searchStartTime = timeManager.ElapsedMs();

                while (failCount < MAX_ASPIRATION_RETRIES && !stopSearch)
                {
                    bestScore = SearchRoot(depth, alpha, beta, rootMoves);

                    if (stopSearch)
                        break;

                    // Check aspiration window failure
                    if (bestScore <= alpha)
                    {
                        beta = (alpha + beta) / 2;
                        alpha = Math.Max(-INFINITY, bestScore - aspWindow);
                        aspWindow = aspWindow + aspWindow / 2;
                        failCount++;
                    }
                    else if (bestScore >= beta)
                    {
                        beta = (alpha + beta) / 2;
                        beta = Math.Min(INFINITY, bestScore + aspWindow);
                        aspWindow = aspWindow + aspWindow / 2;
                        failCount++;
                    }
                    else
                    {
                        break;
                    }

                    // Safety checks
                    if (ShouldStopSearch())
                        break;

                    var elapsedAtThisDepth = timeManager.ElapsedMs() - searchStartTime;
                    var timeLimit = limits.MoveTime > 0 ? limits.MoveTime / 4 : 1000;

                    if (failCount >= MAX_ASPIRATION_RETRIES || elapsedAtThisDepth > timeLimit)
                    {
                        if (alpha != -INFINITY || beta != INFINITY)
                        {
                            alpha = -INFINITY;
                            beta = INFINITY;
                        }
                        break;
                    }
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

                    // Early exit conditions
                    if (Math.Abs(bestScore) >= MATE_VALUE - 100)
                    {
                        break;
                    }

                    if (timeManager.ShouldStop())
                    {
                        break;
                    }

                    if (depth >= 6 && timeManager.ElapsedMs() > timeManager.GetAllocatedTime() / 2)
                    {
                        var timePerDepth = timeManager.ElapsedMs() / depth;
                        var estimatedNextDepthTime = timePerDepth * 2;

                        if (timeManager.ElapsedMs() + estimatedNextDepthTime > timeManager.GetAllocatedTime())
                        {
                            break;
                        }
                    }
                }
                else
                {
                    break;
                }
            }

            return searchResult;
        }

        private int SearchRoot(int depth, int alpha, int beta, List<RootMove> rootMoves)
        {
            if (stopSearch) return 0;

            var bestScore = -INFINITY;
            var bestMoveIndex = -1;  // Index of move that beat alpha
            var fallbackMoveIndex = 0;  // Best move when all fail low
            pvLength[0] = 0;

            // Sort root moves by previous iteration scores
            rootMoves.Sort((a, b) => b.PreviousScore.CompareTo(a.PreviousScore));

            for (int moveIndex = 0; moveIndex < rootMoves.Count && !stopSearch; moveIndex++)
            {
                var rootMove = rootMoves[moveIndex];
                NodesSearched++;

                // Make move
                var colorToMove = rootPosition.Turn;
                rootPosition.Play(colorToMove, rootMove.Move);

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
                rootPosition.Undo(colorToMove, rootMove.Move);

                if (stopSearch)
                    break;

                rootMove.Score = score;
                rootMove.PreviousScore = rootMove.Score;

                if (score > bestScore)
                {
                    bestScore = score;
                    fallbackMoveIndex = moveIndex;  // Always track best move

                    if (score > alpha)
                    {
                        alpha = score;
                        bestMoveIndex = moveIndex;  // Track move that beat alpha
                                                    // Update PV
                        UpdatePV(rootMove.Move, 0);

                        if (score >= beta)
                            break;
                    }
                }
            }

            // Move best move to front
            // Use bestMoveIndex if we found a move that beat alpha, otherwise use fallback
            int moveToPromote = bestMoveIndex >= 0 ? bestMoveIndex : fallbackMoveIndex;

            if (moveToPromote > 0 && moveToPromote < rootMoves.Count)
            {
                var bestMove = rootMoves[moveToPromote];
                rootMoves.RemoveAt(moveToPromote);
                rootMoves.Insert(0, bestMove);
            }

            return bestScore;
        }

        private int AlphaBeta(int depth, int alpha, int beta, int ply, bool pvNode)
        {
            // Check stop flag immediately
            if (stopSearch)
                return 0;

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
                    if (ttScore >= beta && !ttMove.IsCapture)
                    {
                        UpdateKillers(ttMove, ply);
                        UpdateHistory(ttMove, depth);
                    }
                    return ttScore;
                }
            }

            // Drop into quiescence search at leaf nodes
            if (depth <= 0)
                return Quiescence(alpha, beta, ply, 0);

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
                staticEval = ttEntry.Value.Score;
                staticEvalStack[ply] = staticEval;
            }
            else
            {
                staticEval = Evaluate();
                staticEvalStack[ply] = staticEval;
            }

            // Improving
            bool improving = ply >= 2 && !inCheck &&
                           staticEval > staticEvalStack[ply - 2];

            // Reverse futility pruning
            if (!pvNode && !inCheck && depth <= 8 &&
                staticEval - 75 * depth >= beta &&
                staticEval < MATE_VALUE - MAX_PLY)
            {
                return staticEval;
            }

            // Null move pruning
            if (!pvNode && !inCheck && depth >= 3 && staticEval >= beta &&
                ply > 0 && HasNonPawnMaterial() && !stopSearch)
            {
                int R = 3 + depth / 6 + Math.Min(3, (staticEval - beta) / 200);
                R = Math.Min(depth - 1, R);

                rootPosition.MakeNullMove();
                int nullScore = -AlphaBeta(depth - R - 1, -beta, -beta + 1, ply + 1, false);
                rootPosition.UnmakeNullMove();

                if (stopSearch)
                    return 0;

                if (nullScore >= beta)
                {
                    if (nullScore >= MATE_VALUE - MAX_PLY)
                        nullScore = beta;

                    return nullScore;
                }
            }

            // Internal iterative deepening
            if (pvNode && depth >= 6 && ttMove.From == ttMove.To && !stopSearch)
            {
                AlphaBeta(depth - 2, alpha, beta, ply, true);
                ttEntry = tt.Probe(rootPosition.GetHash());
                if (ttEntry.HasValue)
                    ttMove = ttEntry.Value.Move;
            }

            // Generate moves
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

            for (int i = 0; i < moveCount && !stopSearch; i++)
            {
                var move = moves[i];
                NodesSearched++;

                // Futility pruning
                if (!pvNode && !inCheck && !move.IsCapture && depth <= 8 && quietMovesSeen > 0)
                {
                    if (staticEval + 50 + 30 * depth <= alpha)
                        continue;
                }

                // Late move pruning
                if (!pvNode && !inCheck && quietMovesSeen > 0 && depth <= 8)
                {
                    int lmpThreshold = 3 + depth * depth;
                    if (!improving) lmpThreshold /= 2;

                    if (quietMovesSeen >= lmpThreshold)
                        continue;
                }

                // Make move
                var colorToMove = rootPosition.Turn;
                rootPosition.Play(colorToMove, move);

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

                        if (move == killerMoves[ply, 0] || move == killerMoves[ply, 1])
                            reduction--;

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
                rootPosition.Undo(colorToMove, move);

                if (stopSearch)
                    break;

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
            }

            // Store in transposition table
            if (!stopSearch)
            {
                var flag = bestScore >= beta ? TTFlag.LowerBound :
                          bestScore <= alpha ? TTFlag.UpperBound : TTFlag.Exact;

                tt.Store(rootPosition.GetHash(), depth, ScoreToTT(bestScore, ply), flag, bestMove);
            }

            return bestScore;
        }

        private int Quiescence(int alpha, int beta, int ply, int qDepth)
        {
            // Check stop flag
            if (stopSearch)
                return 0;

            // Check time periodically
            if ((QNodes & 2047) == 0 && ShouldStopSearch())
                return 0;

            QNodes++;
            pvLength[ply] = ply;

            // Update selective depth
            if (ply > SelectiveDepth)
                SelectiveDepth = ply;

            // Terminal node checks
            if (ply >= MAX_PLY)
                return Evaluate();

            // Check for draw
            if (IsDrawByRepetition())
                return DRAW_VALUE;

            // Transposition table probe
            var ttEntry = tt.Probe(rootPosition.GetHash());
            var ttHit = ttEntry.HasValue;
            var ttMove = ttEntry?.Move ?? new Move.Move();

            if (ttHit && ttEntry.Value.Depth >= QSEARCH_DEPTH + qDepth)
            {
                var entry = ttEntry.Value;
                var ttScore = ScoreFromTT(entry.Score, ply);

                if (entry.Flag == TTFlag.Exact ||
                    (entry.Flag == TTFlag.LowerBound && ttScore >= beta) ||
                    (entry.Flag == TTFlag.UpperBound && ttScore <= alpha))
                {
                    return ttScore;
                }
            }

            var inCheck = rootPosition.InCheck(rootPosition.Turn);
            int staticEval;

            // In check - must search all evasions
            if (inCheck)
            {
                staticEval = -INFINITY;
            }
            else
            {
                // Stand pat - static evaluation as lower bound
                staticEval = ttHit && ttEntry.Value.Score != -INFINITY ?
                            ttEntry.Value.Score : Evaluate();

                // Stand pat cutoff
                if (staticEval >= beta)
                    return staticEval;

                // Update alpha (standing pat acts as a lower bound)
                if (staticEval > alpha)
                    alpha = staticEval;
            }

            // Generate moves
            var moves = moveBuffers[ply];
            var moveCount = inCheck ?
                GenerateMovesInto(moves) :  // All moves when in check
                GenerateCapturesInto(moves, false);  // Only captures when not in check

            if (moveCount == 0)
            {
                return inCheck ? -MATE_VALUE + ply : staticEval;
            }

            // Order captures
            moveCount = OrderCapturesInPlace(moves, moveCount, ply);

            var bestScore = staticEval;
            var bestMove = new Move.Move();

            for (int i = 0; i < moveCount && !stopSearch; i++)
            {
                var move = moves[i];

                // Delta pruning - skip obviously bad captures
                if (!inCheck && staticEval < alpha - 200)
                {
                    var capturedValue = GetMaterialValue(rootPosition.At(move.To));

                    // Add potential promotion value
                    if ((move.Flags & MoveFlags.Promotions) != 0)
                    {
                        capturedValue += GetPromotionValue(move.Flags) - 100; // Pawn value
                    }

                    // Skip if capture can't raise alpha
                    if (staticEval + capturedValue + 200 < alpha)
                        continue;
                }

                // SEE pruning for non-pawn captures
                if (!inCheck && move.IsCapture &&
                    Types.TypeOf(rootPosition.At(move.From)) != PieceType.Pawn)
                {
                    if (!SEEGreaterOrEqual(move, 0))
                        continue;
                }

                QNodes++;

                // Make move
                var colorToMove = rootPosition.Turn;
                rootPosition.Play(colorToMove, move);

                // Recursively search
                var score = -Quiescence(-beta, -alpha, ply + 1, qDepth - 1);

                // Unmake move
                rootPosition.Undo(colorToMove, move);

                if (stopSearch)
                    break;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;

                    if (score > alpha)
                    {
                        alpha = score;
                        UpdatePV(move, ply);

                        if (score >= beta)
                            break;
                    }
                }
            }

            // Store in TT
            if (!stopSearch)
            {
                var flag = bestScore >= beta ? TTFlag.LowerBound :
                          bestScore <= alpha ? TTFlag.UpperBound : TTFlag.Exact;

                tt.Store(rootPosition.GetHash(), QSEARCH_DEPTH + qDepth,
                        ScoreToTT(bestScore, ply), flag, bestMove);
            }

            return bestScore;
        }

        // Simple Static Exchange Evaluation
        private bool SEEGreaterOrEqual(Move.Move move, int threshold)
        {
            var from = move.From;
            var to = move.To;

            // Get initial material balance
            var capturedPiece = rootPosition.At(to);
            var attackingPiece = rootPosition.At(from);

            if (capturedPiece == Piece.NoPiece && move.Flags != MoveFlags.EnPassant)
                return true; // Non-capture

            var gain = GetMaterialValue(capturedPiece);

            // Promotion bonus
            if ((move.Flags & MoveFlags.Promotions) != 0)
            {
                gain += GetPromotionValue(move.Flags) - 100; // Subtract pawn value
            }

            // If we can't even beat threshold with the initial capture, fail
            if (gain < threshold)
                return false;

            // Simple approximation: if captured piece is more valuable than attacker, it's good
            var attackerValue = GetMaterialValue(attackingPiece);
            if (gain >= attackerValue)
                return true;

            // More complex SEE would simulate the capture sequence here
            // For now, use a simple heuristic
            var balance = gain - attackerValue;

            // Check if the destination square is defended
            var occupancy = rootPosition.AllPieces(Color.White) | rootPosition.AllPieces(Color.Black);
            occupancy &= ~(1UL << (int)from); // Remove attacker

            var enemyColor = Types.ColorOf(attackingPiece).Flip();
            var defenders = rootPosition.AttackersFrom(enemyColor, to, occupancy);

            // If defended, assume we lose our piece
            if (defenders != 0)
            {
                // Find least valuable defender
                var minDefenderValue = 10000;
                if ((defenders & rootPosition.BitboardOf(enemyColor, PieceType.Pawn)) != 0)
                    minDefenderValue = 100;
                else if ((defenders & rootPosition.BitboardOf(enemyColor, PieceType.Knight)) != 0)
                    minDefenderValue = 320;
                else if ((defenders & rootPosition.BitboardOf(enemyColor, PieceType.Bishop)) != 0)
                    minDefenderValue = 330;
                else if ((defenders & rootPosition.BitboardOf(enemyColor, PieceType.Rook)) != 0)
                    minDefenderValue = 500;
                else if ((defenders & rootPosition.BitboardOf(enemyColor, PieceType.Queen)) != 0)
                    minDefenderValue = 900;

                balance = balance - attackerValue + minDefenderValue;
            }

            return balance >= threshold;
        }

        private int GetMaterialValue(Piece piece)
        {
            if (piece == Piece.NoPiece)
                return 0;

            return Types.TypeOf(piece) switch
            {
                PieceType.Pawn => 100,
                PieceType.Knight => 320,
                PieceType.Bishop => 330,
                PieceType.Rook => 500,
                PieceType.Queen => 900,
                PieceType.King => 10000,
                _ => 0
            };
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
            return rootPosition.IsFiftyMoveRule();
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

        // Move generation helpers
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

        // Move ordering
        private int OrderMovesInPlace(Move.Move[] moves, int moveCount, Move.Move ttMove, int ply, Move.Move prevMove = default)
        {
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

        // Evaluation
        private int Evaluate()
        {
            return Evaluation.Evaluate(rootPosition);
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
            if (stopSearch)
                return true;

            return timeManager.ShouldStop();
        }

        public void StopSearch()
        {
            stopSearch = true;
            timeManager.ForceStop();
        }

        private void PrintSearchInfo(SearchResult result)
        {
            Console.WriteLine($"info depth {result.Depth} seldepth {SelectiveDepth} score cp {result.Score} " +
                            $"nodes {result.Nodes} qnodes {QNodes} nps {result.Nodes * 1000 / (ulong)Math.Max(1, result.Time)} " +
                            $"time {result.Time} hashfull {tt.HashFull()} pv {string.Join(" ", result.Pv)}");
            Console.Out.Flush();
        }
    }

    // Helper classes remain unchanged...
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