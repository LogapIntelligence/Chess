namespace Chess;

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

public class Search
{
    public const int MaxDepth = 64;
    private const int Infinity = 1000000;
    private const int MateScore = 100000;
    private const int DrawScore = 0;

    private readonly TranspositionTable _tt;
    private readonly MoveOrdering _moveOrdering;
    private readonly Stopwatch _timer;

    private long _nodes;
    private long _timeAllocated;
    private bool _stop;
    private int _selDepth;

    // Optimization: Check time less frequently
    private const int TimeCheckMask = 0x3FFF; // Check every 16384 nodes

    private long _lastInfoTime;
    private const long InfoInterval = 1000;

    // Principal variation
    private readonly Move[,] _pvTable = new Move[MaxDepth, MaxDepth];
    private readonly int[] _pvLength = new int[MaxDepth];

    private Move _iterationBestMove;
    private int _iterationBestScore;
    private int _hashfull = 0;

    // History for repetition detection  
    private readonly ulong[] _hashHistory = new ulong[1024];
    private int _hashHistoryCount = 0;

    // Pre-allocated for move generation
    private readonly MoveList[] _moveLists = new MoveList[MaxDepth];

    // Futility margins
    private static readonly int[] FutilityMargins = { 0, 100, 200, 300, 400, 500 };

    public Search(int ttSizeMb = 128)
    {
        _tt = new TranspositionTable(ttSizeMb);
        _moveOrdering = new MoveOrdering();
        _timer = new Stopwatch();
    }

    public Move Think(ref Board board, long timeMs, int maxDepth = MaxDepth)
    {
        _timer.Restart();
        _timeAllocated = timeMs;
        _stop = false;
        _nodes = 0;
        _selDepth = 0;
        _lastInfoTime = 0;
        _hashfull = 0;
        _hashHistoryCount = 0;

        // Add initial position to history
        _hashHistory[_hashHistoryCount++] = board.GetZobristHash();

        Move bestMove = default;
        int bestScore = -Infinity;

        // Generate initial moves
        MoveList initialMoves = new MoveList();
        MoveGenerator.GenerateMoves(ref board, ref initialMoves);
        if (initialMoves.Count > 0)
        {
            bestMove = initialMoves[0];
        }

        // Aspiration window
        int alpha = -Infinity;
        int beta = Infinity;
        int aspirationDelta = 50;

        // Iterative deepening
        for (int depth = 1; depth <= maxDepth && !_stop; depth++)
        {
            _selDepth = 0;
            _iterationBestMove = bestMove;
            _iterationBestScore = bestScore;

            Array.Clear(_pvLength, 0, _pvLength.Length);
            Array.Clear(_pvTable, 0, _pvTable.Length);

            // Aspiration window search
            if (depth >= 4 && Math.Abs(bestScore) < MateScore - 1000)
            {
                alpha = bestScore - aspirationDelta;
                beta = bestScore + aspirationDelta;
            }
            else
            {
                alpha = -Infinity;
                beta = Infinity;
            }

            while (true)
            {
                int score = AlphaBeta(ref board, depth, alpha, beta, 0, true);

                if (_stop) break;

                // Handle aspiration window failures
                if (score <= alpha)
                {
                    alpha = Math.Max(alpha - aspirationDelta, -Infinity);
                    aspirationDelta = Math.Min(aspirationDelta * 2, 500);
                }
                else if (score >= beta)
                {
                    beta = Math.Min(beta + aspirationDelta, Infinity);
                    aspirationDelta = Math.Min(aspirationDelta * 2, 500);
                }
                else
                {
                    bestScore = score;
                    if (_pvLength[0] > 0)
                    {
                        bestMove = _pvTable[0, 0];
                    }
                    break;
                }
            }

            if (!_stop)
            {
                SendInfo(depth, _selDepth, bestScore, GetPvString());
            }

            // Time management
            if (_timeAllocated != long.MaxValue)
            {
                long elapsed = _timer.ElapsedMilliseconds;

                // Stop early if we've used significant time
                if (elapsed >= _timeAllocated * 0.4 && depth >= 4)
                {
                    _stop = true;
                    break;
                }

                if (elapsed >= _timeAllocated)
                {
                    _stop = true;
                    break;
                }
            }
        }

        if (bestMove != default)
        {
            Console.WriteLine($"bestmove {bestMove}");
        }
        else
        {
            Console.WriteLine("bestmove 0000");
        }
        Console.Out.Flush();

        return bestMove;
    }

    private int AlphaBeta(ref Board board, int depth, int alpha, int beta, int ply, bool isPvNode)
    {
        _pvLength[ply] = ply;

        // Check time less frequently
        if ((_nodes & TimeCheckMask) == 0)
        {
            long currentTime = _timer.ElapsedMilliseconds;

            if (_timeAllocated != long.MaxValue && currentTime > _timeAllocated)
            {
                _stop = true;
                return 0;
            }

            // Send periodic info for long searches
            if (ply == 0 && currentTime - _lastInfoTime >= InfoInterval)
            {
                SendPeriodicInfo();
            }
        }

        // Quick draw detection
        if (ply > 0)
        {
            if (board.HalfmoveClock >= 100)
                return DrawScore;

            // Simplified repetition check
            ulong currentHash = board.GetZobristHash();
            int repCount = 0;
            for (int i = _hashHistoryCount - 2; i >= 0 && i >= _hashHistoryCount - board.HalfmoveClock; i -= 2)
            {
                if (_hashHistory[i] == currentHash)
                {
                    repCount++;
                    if (repCount >= 2)
                        return DrawScore;
                }
            }
        }

        // Mate distance pruning
        int matedScore = -MateScore + ply;
        int mateScore = MateScore - ply - 1;
        if (matedScore >= beta) return beta;
        if (mateScore <= alpha) return alpha;
        alpha = Math.Max(alpha, matedScore);
        beta = Math.Min(beta, mateScore);

        // Transposition table probe
        ulong hash = board.GetZobristHash();
        var ttEntry = _tt.Probe(hash);
        Move ttMove = default;

        if (ttEntry.Hash == hash && ttEntry.Depth >= depth && !isPvNode)
        {
            ttMove = ttEntry.Move;
            int ttScore = ttEntry.Score;

            // Adjust mate scores
            if (ttScore > MateScore - 100)
                ttScore -= ply;
            else if (ttScore < -MateScore + 100)
                ttScore += ply;

            if (ttEntry.Flag == TTFlag.Exact)
                return ttScore;
            else if (ttEntry.Flag == TTFlag.LowerBound)
                alpha = Math.Max(alpha, ttScore);
            else if (ttEntry.Flag == TTFlag.UpperBound)
                beta = Math.Min(beta, ttScore);

            if (alpha >= beta)
                return ttScore;
        }

        if (ttEntry.Hash == hash)
            ttMove = ttEntry.Move;

        // Drop into quiescence
        if (depth <= 0)
            return Quiescence(ref board, alpha, beta, ply);

        _nodes++;
        if (ply > _selDepth)
            _selDepth = ply;

        bool inCheck = board.IsInCheckFast();
        int eval = inCheck ? -Infinity : Evaluation.Evaluate(ref board);

        // Reverse futility pruning (static null move pruning)
        if (!isPvNode && !inCheck && depth <= 6 &&
            eval - FutilityMargins[depth] >= beta)
        {
            return eval;
        }

        // Null move pruning
        if (!isPvNode && !inCheck && depth >= 3 && ply > 0 &&
            board.HasNonPawnMaterial() && eval >= beta)
        {
            int R = 3 + depth / 6 + Math.Min((eval - beta) / 200, 3);

            Board nullBoard = board;
            nullBoard.SideToMove ^= (Color)1;
            nullBoard.EnPassantSquare = -1;
            nullBoard.HalfmoveClock++;
            if (nullBoard.SideToMove == Color.Black)
                nullBoard.FullmoveNumber++;

            int nullScore = -AlphaBeta(ref nullBoard, depth - R - 1, -beta, -beta + 1, ply + 1, false);

            if (_stop) return 0;

            if (nullScore >= beta)
            {
                // Verification at high depths
                if (depth > 12)
                {
                    int verifyScore = AlphaBeta(ref board, depth - R, beta - 1, beta, ply, false);
                    if (verifyScore >= beta)
                        return beta;
                }
                else
                {
                    return beta;
                }
            }
        }

        // Generate moves
        MoveList moves = new MoveList();
        MoveGenerator.GenerateMoves(ref board, ref moves);

        if (moves.Count == 0)
        {
            return inCheck ? -MateScore + ply : DrawScore;
        }

        // Order moves
        _moveOrdering.OrderMoves(ref board, ref moves, ttMove, ply);

        Move bestMove = default;
        int bestScore = -Infinity;
        TTFlag flag = TTFlag.UpperBound;
        int movesSearched = 0;

        for (int i = 0; i < moves.Count; i++)
        {
            Move move = moves[i];

            // Send current move info at root
            if (ply == 0 && depth > 5 && i > 0 && _timer.ElapsedMilliseconds > 500)
            {
                SendCurrMoveInfo(move, i + 1);
            }

            // Futility pruning
            if (!isPvNode && !inCheck && !move.IsCapture && !move.IsPromotion &&
                depth <= 3 && movesSearched >= 4 &&
                eval + FutilityMargins[depth] + 100 <= alpha)
            {
                continue;
            }

            Board newBoard = board;
            newBoard.MakeMove(move);

            _hashHistory[_hashHistoryCount++] = newBoard.GetZobristHash();

            int score;

            // Principal variation search with reductions
            if (movesSearched == 0)
            {
                score = -AlphaBeta(ref newBoard, depth - 1, -beta, -alpha, ply + 1, isPvNode);
            }
            else
            {
                int reduction = 0;

                // Late move reduction
                if (depth >= 3 && movesSearched >= 3 && !move.IsCapture &&
                    !move.IsPromotion && !inCheck && !newBoard.IsInCheckFast())
                {
                    reduction = 1;
                    if (movesSearched >= 6) reduction++;
                    if (depth >= 6 && movesSearched >= 10) reduction++;

                    reduction = Math.Min(reduction, depth - 2);
                }

                // Search with null window
                score = -AlphaBeta(ref newBoard, depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, false);

                // Re-search if needed
                if (score > alpha && (reduction > 0 || score < beta))
                {
                    score = -AlphaBeta(ref newBoard, depth - 1, -beta, -alpha, ply + 1, isPvNode);
                }
            }

            _hashHistoryCount--;
            movesSearched++;

            if (_stop) return 0;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;

                if (score > alpha)
                {
                    alpha = score;
                    flag = TTFlag.Exact;

                    UpdatePV(ply, move);

                    if (ply == 0)
                    {
                        _iterationBestMove = move;
                        _iterationBestScore = score;
                    }

                    if (score >= beta)
                    {
                        flag = TTFlag.LowerBound;

                        if (!move.IsCapture)
                        {
                            _moveOrdering.UpdateKillers(move, ply);
                            _moveOrdering.UpdateHistory(move, depth);
                        }

                        break;
                    }
                }
            }
        }

        // Store in TT
        int storeScore = bestScore;
        if (bestScore > MateScore - 100)
            storeScore += ply;
        else if (bestScore < -MateScore + 100)
            storeScore -= ply;

        _tt.Store(hash, depth, storeScore, flag, bestMove);

        return bestScore;
    }

    private int Quiescence(ref Board board, int alpha, int beta, int ply)
    {
        _nodes++;

        if ((_nodes & 0x1FFF) == 0 && _timeAllocated != long.MaxValue &&
            _timer.ElapsedMilliseconds > _timeAllocated)
        {
            _stop = true;
            return 0;
        }

        int standPat = Evaluation.Evaluate(ref board);

        if (standPat >= beta)
            return beta;

        // Delta pruning
        const int BigDelta = 900;
        if (standPat + BigDelta < alpha)
            return alpha;

        if (standPat > alpha)
            alpha = standPat;

        // Generate all moves and filter captures
        MoveList moves = new MoveList();
        MoveGenerator.GenerateMoves(ref board, ref moves);

        // Score captures with MVV-LVA
        for (int i = 0; i < moves.Count; i++)
        {
            Move move = moves[i];

            if (!move.IsCapture && !move.IsPromotion)
                continue;

            Board newBoard = board;
            newBoard.MakeMove(move);

            int score = -Quiescence(ref newBoard, -beta, -alpha, ply + 1);

            if (_stop) return 0;

            if (score >= beta)
                return beta;

            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    private void UpdatePV(int ply, Move move)
    {
        _pvTable[ply, ply] = move;

        for (int i = ply + 1; i < _pvLength[ply + 1]; i++)
        {
            _pvTable[ply, i] = _pvTable[ply + 1, i];
        }

        _pvLength[ply] = _pvLength[ply + 1];
    }

    private void SendInfo(int depth, int selDepth, int score, string pv)
    {
        long time = _timer.ElapsedMilliseconds;
        long nps = time > 0 ? (_nodes * 1000) / time : _nodes;

        string scoreStr = FormatScore(score);
        _hashfull = _tt.Usage();

        Console.WriteLine($"info depth {depth} seldepth {selDepth} score {scoreStr} " +
                        $"nodes {_nodes} nps {nps} hashfull {_hashfull} " +
                        $"time {time} pv {pv}");
        Console.Out.Flush();
    }

    private void SendPeriodicInfo()
    {
        long time = _timer.ElapsedMilliseconds;

        _lastInfoTime = time;
        long nps = time > 0 ? (_nodes * 1000) / time : _nodes;

        Console.WriteLine($"info nodes {_nodes} nps {nps} time {time}");
        Console.Out.Flush();
    }

    private void SendCurrMoveInfo(Move move, int moveNumber)
    {
        Console.WriteLine($"info currmove {move} currmovenumber {moveNumber}");
        Console.Out.Flush();
    }

    private string FormatScore(int score)
    {
        if (Math.Abs(score) >= MateScore - 1000)
        {
            int mateIn = (MateScore - Math.Abs(score) + 1) / 2;
            return score > 0 ? $"mate {mateIn}" : $"mate -{mateIn}";
        }
        return $"cp {score}";
    }

    private string GetPvString()
    {
        var pv = new System.Text.StringBuilder();

        for (int i = 0; i < _pvLength[0]; i++)
        {
            Move move = _pvTable[0, i];
            if (move == default)
                break;

            if (i > 0) pv.Append(" ");
            pv.Append(move.ToString());
        }

        return pv.ToString();
    }

    public void Stop()
    {
        _stop = true;
    }

    public void ClearHash()
    {
        _tt.Clear();
        _moveOrdering.ClearHistory();
        _moveOrdering.ClearKillers();
    }
}