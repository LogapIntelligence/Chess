using System;
using System.Threading;
using System.Threading.Tasks;
using Move;

namespace Search
{
    unsafe
    public class Search
    {
        // Constants
        public const int MAX_PLY = 128;
        public const int INFINITY = 30000;
        public const int MATE_VALUE = 29000;
        public const int DRAW_VALUE = 0;

        // Search state
        private Position rootPosition;
        private readonly SearchInfo searchInfo;
        private readonly TranspositionTable tt;
        private readonly MoveOrdering moveOrdering;
        private readonly ThreadPool threadPool;

        // Statistics
        public ulong NodesSearched { get; private set; }
        public int SelectiveDepth { get; private set; }

        // Principal Variation
        private readonly Move.Move[,] pvTable;
        private readonly int[] pvLength;

        // Killer moves and history heuristic
        private readonly Move.Move[,] killerMoves;
        private readonly int[,] historyTable;

        // Time management
        private readonly TimeManager timeManager;
        private CancellationTokenSource? searchCancellation;

        public Search(int ttSizeMB = 128, int threadCount = 1)
        {
            searchInfo = new SearchInfo();
            tt = new TranspositionTable(ttSizeMB);
            moveOrdering = new MoveOrdering();
            threadPool = new ThreadPool(threadCount);
            timeManager = new TimeManager();

            pvTable = new Move.Move[MAX_PLY, MAX_PLY];
            pvLength = new int[MAX_PLY];
            killerMoves = new Move.Move[MAX_PLY, 2];
            historyTable = new int[64, 64];

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
                var aspWindow = 50;
                var alpha = -INFINITY;
                var beta = INFINITY;

                // Aspiration window
                if (depth >= 5 && Math.Abs(bestScore) < MATE_VALUE - 100)
                {
                    alpha = bestScore - aspWindow;
                    beta = bestScore + aspWindow;
                }

                // Search with aspiration window
                while (true)
                {
                    bestScore = SearchRoot(depth, alpha, beta, rootMoves);

                    // Check aspiration window failure
                    if (bestScore <= alpha)
                    {
                        alpha = Math.Max(-INFINITY, alpha - aspWindow);
                        aspWindow *= 2;
                    }
                    else if (bestScore >= beta)
                    {
                        beta = Math.Min(INFINITY, beta + aspWindow);
                        aspWindow *= 2;
                    }
                    else
                    {
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
                }
            }

            return searchResult;
        }

        private int SearchRoot(int depth, int alpha, int beta, List<RootMove> rootMoves)
        {
            var bestScore = -INFINITY;
            pvLength[0] = 0;

            foreach (var rootMove in rootMoves)
            {
                NodesSearched++;

                // Make move
                rootPosition.Play(rootPosition.Turn, rootMove.Move);

                int score;
                if (rootMove == rootMoves[0])
                {
                    // Full window search for first move
                    score = -AlphaBeta(depth - 1, -beta, -alpha, 1, true);
                }
                else
                {
                    // Late move reduction
                    int reduction = 0;
                    if (depth >= 3 && !rootMove.Move.IsCapture)
                    {
                        reduction = 1;
                        if (rootMoves.IndexOf(rootMove) > 5)
                            reduction = 2;
                    }

                    // Null window search
                    score = -AlphaBeta(depth - 1 - reduction, -alpha - 1, -alpha, 1, true);

                    // Re-search if needed
                    if (score > alpha && score < beta)
                        score = -AlphaBeta(depth - 1, -beta, -alpha, 1, true);
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

                        // Update PV
                        UpdatePV(rootMove.Move, 0);

                        // Move best move to front
                        rootMoves.Remove(rootMove);
                        rootMoves.Insert(0, rootMove);

                        if (score >= beta)
                            break;
                    }
                }

                if (ShouldStopSearch())
                    break;
            }

            return bestScore;
        }

        private int AlphaBeta(int depth, int alpha, int beta, int ply, bool pvNode)
        {
            // Check time and stop conditions
            if ((NodesSearched & 2047) == 0 && ShouldStopSearch())
                return 0;

            pvLength[ply] = ply;

            // Terminal node checks
            if (ply >= MAX_PLY)
                return Evaluate();

            // Check for draw
            if (IsDrawByRepetition() || rootPosition.History[rootPosition.Ply].Entry == 0)
                return DRAW_VALUE;

            // Mate distance pruning
            alpha = Math.Max(alpha, -MATE_VALUE + ply);
            beta = Math.Min(beta, MATE_VALUE - ply - 1);
            if (alpha >= beta)
                return alpha;

            // Transposition table probe
            var ttEntry = tt.Probe(rootPosition.GetHash());
            var ttMove = ttEntry?.Move ?? new Move.Move();

            if (ttEntry.HasValue && ttEntry.Value.Depth >= depth && !pvNode)
            {
                var entry = ttEntry.Value;
                var ttScore = ScoreFromTT(entry.Score, ply);

                if (entry.Flag == TTFlag.Exact)
                    return ttScore;
                else if (entry.Flag == TTFlag.LowerBound && ttScore >= beta)
                    return ttScore;
                else if (entry.Flag == TTFlag.UpperBound && ttScore <= alpha)
                    return ttScore;
            }

            // Quiescence search at leaf nodes
            if (depth <= 0)
                return Quiescence(alpha, beta, ply);

            var inCheck = rootPosition.InCheck(rootPosition.Turn);
            var staticEval = inCheck ? -INFINITY : Evaluate();

            // Null move pruning
            if (!pvNode && !inCheck && depth >= 3 && staticEval >= beta)
            {
                var R = 3 + depth / 4;
                rootPosition.Play(rootPosition.Turn, new Move.Move());
                var nullScore = -AlphaBeta(depth - R - 1, -beta, -beta + 1, ply + 1, false);
                rootPosition.Undo(rootPosition.Turn.Flip(), new Move.Move());

                if (nullScore >= beta)
                    return beta;
            }

            // Generate and order moves
            var moves = GenerateMoves();
            var moveCount = OrderMoves(moves, ttMove, ply);

            if (moveCount == 0)
                return inCheck ? -MATE_VALUE + ply : DRAW_VALUE;

            var bestScore = -INFINITY;
            var bestMove = new Move.Move();
            var movesSearched = 0;

            for (int i = 0; i < moveCount; i++)
            {
                var move = moves[i];
                NodesSearched++;

                // Make move
                rootPosition.Play(rootPosition.Turn, move);

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
                    if (depth >= 3 && movesSearched >= 4 && !move.IsCapture && !inCheck)
                    {
                        reduction = 1;
                        if (movesSearched >= 8)
                            reduction = 2;
                    }

                    // Null window search
                    score = -AlphaBeta(depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, false);

                    // Re-search if needed
                    if (score > alpha && score < beta)
                        score = -AlphaBeta(depth - 1, -beta, -alpha, ply + 1, pvNode);
                }

                // Unmake move
                rootPosition.Undo(rootPosition.Turn.Flip(), move);

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
                            // Update killer moves
                            if (!move.IsCapture)
                            {
                                killerMoves[ply, 1] = killerMoves[ply, 0];
                                killerMoves[ply, 0] = move;

                                // Update history
                                historyTable[(int)move.From, (int)move.To] += depth * depth;
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
                if (standPat > alpha)
                    alpha = standPat;
            }

            // Generate captures only (or all moves if in check)
            var moves = GenerateCaptures(inCheck);
            var moveCount = OrderCaptures(moves, ply);

            if (inCheck && moveCount == 0)
                return -MATE_VALUE + ply;

            for (int i = 0; i < moveCount; i++)
            {
                var move = moves[i];

                // Delta pruning
                if (!inCheck && standPat + GetPieceValue(rootPosition.At(move.To)) + 200 < alpha)
                    continue;

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

        // Move generation helpers
        private unsafe Move.Move[] GenerateMoves()
        {
            var moves = new Move.Move[256];
            int count;

            fixed (Move.Move* movesPtr = moves)
            {
                if (rootPosition.Turn == Color.White)
                    count = rootPosition.GenerateLegalsInto<White>(movesPtr);
                else
                    count = rootPosition.GenerateLegalsInto<Black>(movesPtr);
            }

            Array.Resize(ref moves, count);
            return moves;
        }

        private unsafe Move.Move[] GenerateCaptures(bool inCheck)
        {
            if (inCheck)
                return GenerateMoves();

            var allMoves = GenerateMoves();
            return Array.FindAll(allMoves, m => m.IsCapture);
        }

        private List<RootMove> GenerateRootMoves()
        {
            var moves = GenerateMoves();
            var rootMoves = new List<RootMove>();

            foreach (var move in moves)
                rootMoves.Add(new RootMove { Move = move });

            return rootMoves;
        }

        // Move ordering
        private int OrderMoves(Move.Move[] moves, Move.Move ttMove, int ply)
        {
            return moveOrdering.OrderMoves(moves, ttMove, killerMoves[ply, 0],
                                         killerMoves[ply, 1], historyTable, rootPosition);
        }

        private int OrderCaptures(Move.Move[] moves, int ply)
        {
            return moveOrdering.OrderCaptures(moves, rootPosition);
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
            var currentHash = rootPosition.GetHash();
            var count = 0;

            for (int i = rootPosition.Ply - 2; i >= 0; i -= 2)
            {
                if (rootPosition.History[i].Entry == currentHash)
                {
                    count++;
                    if (count >= 2)
                        return true;
                }
            }

            return false;
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
            Console.WriteLine($"info depth {result.Depth} score cp {result.Score} " +
                            $"nodes {result.Nodes} nps {result.Nodes * 1000 / (ulong)Math.Max(1, result.Time)} " +
                            $"time {result.Time} pv {string.Join(" ", result.Pv)}");
        }
    }

    // Helper classes
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