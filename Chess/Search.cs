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

    // Add fields for periodic info output
    private long _lastInfoTime;
    private const long InfoInterval = 1000; // Output info every 1000ms

    // Principal variation
    private readonly Move[,] _pvTable = new Move[MaxDepth, MaxDepth];
    private readonly int[] _pvLength = new int[MaxDepth];

    // Best move from completed iteration
    private Move _iterationBestMove;
    private int _iterationBestScore;

    // For hashfull calculation
    private int _hashfull = 0;

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

        Move bestMove = default;
        int bestScore = -Infinity;

        // Generate initial moves to get a valid first move
        MoveList initialMoves = new MoveList();
        MoveGenerator.GenerateMoves(ref board, ref initialMoves);
        if (initialMoves.Count > 0)
        {
            bestMove = initialMoves[0];
        }

        // Iterative deepening
        for (int depth = 1; depth <= maxDepth && !_stop; depth++)
        {
            // Reset for this iteration
            _selDepth = 0;
            _iterationBestMove = bestMove;
            _iterationBestScore = bestScore;

            // Clear PV for new iteration
            Array.Clear(_pvLength, 0, _pvLength.Length);
            Array.Clear(_pvTable, 0, _pvTable.Length);

            int score = AlphaBeta(ref board, depth, -Infinity, Infinity, 0, true);

            if (!_stop)
            {
                bestScore = score;
                if (_pvLength[0] > 0)
                {
                    bestMove = _pvTable[0, 0];
                }

                // Send info after completing each depth
                SendInfo(depth, _selDepth, bestScore, GetPvString());
            }

            // Check time after each iteration
            if (_timeAllocated != long.MaxValue && _timer.ElapsedMilliseconds >= _timeAllocated)
            {
                _stop = true;
                break;
            }
        }

        // Always send bestmove at the end
        if (bestMove != default)
        {
            Console.WriteLine($"bestmove {bestMove}");
        }
        else
        {
            // Emergency fallback
            Console.WriteLine("bestmove 0000");
        }
        Console.Out.Flush();

        return bestMove;
    }

    private void SendInfo(int depth, int selDepth, int score, string pv)
    {
        long time = _timer.ElapsedMilliseconds;
        long nps = time > 0 ? (_nodes * 1000) / time : _nodes;

        string scoreStr = FormatScore(score);

        // Calculate approximate hashfull (simplified)
        _hashfull = Math.Min(999, (int)(_nodes / 1000));

        // Send the actual search information
        Console.WriteLine($"info depth {depth} seldepth {selDepth} score {scoreStr} " +
                        $"nodes {_nodes} nps {nps} hashfull {_hashfull} " +
                        $"time {time} pv {pv}");
        Console.Out.Flush();
    }

    private void SendPeriodicInfo()
    {
        long time = _timer.ElapsedMilliseconds;
        if (time - _lastInfoTime < InfoInterval)
            return;

        _lastInfoTime = time;
        long nps = time > 0 ? (_nodes * 1000) / time : _nodes;

        Console.WriteLine($"info nodes {_nodes} nps {nps} time {time}");
        Console.Out.Flush();
    }

    private void SendCurrMoveInfo(Move move, int moveNumber)
    {
        long time = _timer.ElapsedMilliseconds;
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
        else
        {
            return $"cp {score}";
        }
    }

    private int AlphaBeta(ref Board board, int depth, int alpha, int beta, int ply, bool isPvNode)
    {
        // Initialize PV length for this ply
        _pvLength[ply] = ply;

        // Check for periodic updates
        if ((_nodes & 4095) == 0) // Check every 4096 nodes
        {
            long currentTime = _timer.ElapsedMilliseconds;

            // Check timeout
            if (_timeAllocated != long.MaxValue && currentTime > _timeAllocated)
            {
                _stop = true;
                return 0;
            }

            // Send periodic info for long searches at root
            if (ply == 0 && currentTime - _lastInfoTime >= InfoInterval)
            {
                SendPeriodicInfo();
            }
        }

        // Draw by repetition or 50-move rule
        if (ply > 0 && (board.HalfmoveClock >= 100 || IsRepetition(ref board)))
            return DrawScore;

        // Mate distance pruning
        alpha = Math.Max(alpha, -MateScore + ply);
        beta = Math.Min(beta, MateScore - ply - 1);
        if (alpha >= beta)
            return alpha;

        // Transposition table probe
        ulong hash = board.GetZobristHash();
        var ttEntry = _tt.Probe(hash);
        Move ttMove = default;

        if (ttEntry.Hash == hash && ttEntry.Depth >= depth && !isPvNode)
        {
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

        // Null move pruning
        const int NullMoveReduction = 3;

        if (!isPvNode &&
            !board.IsInCheck() &&
            depth >= NullMoveReduction + 1 &&
            ply > 0 &&
            board.HasNonPawnMaterial())
        {
            // Make null move (just switch sides)
            Board nullBoard = board;
            nullBoard.SideToMove = nullBoard.SideToMove == Color.White ? Color.Black : Color.White;
            nullBoard.EnPassantSquare = -1; // Clear en passant
            nullBoard.HalfmoveClock++;

            // Search with reduced depth
            int nullScore = -AlphaBeta(ref nullBoard, depth - NullMoveReduction - 1, -beta, -beta + 1, ply + 1, false);

            if (_stop)
                return 0;

            // If null move causes a beta cutoff, we can prune
            if (nullScore >= beta)
            {
                // Verification search for high depths to avoid zugzwang
                if (depth > 6)
                {
                    int verifyScore = AlphaBeta(ref board, depth - NullMoveReduction, alpha, beta, ply, false);
                    if (verifyScore >= beta)
                        return beta;
                }
                else
                {
                    return beta;
                }
            }
        }

        // Leaf node - return evaluation
        if (depth <= 0)
            return Quiescence(ref board, alpha, beta, ply);

        _nodes++;
        if (ply > _selDepth)
            _selDepth = ply;

        // Generate and order moves
        MoveList moves = new MoveList();
        MoveGenerator.GenerateMoves(ref board, ref moves);

        if (moves.Count == 0)
        {
            // Checkmate or stalemate
            return board.IsInCheck() ? -MateScore + ply : DrawScore;
        }

        // Order moves
        _moveOrdering.OrderMoves(ref board, ref moves, ttMove, ply);

        Move bestMove = default;
        int bestScore = -Infinity;
        TTFlag flag = TTFlag.UpperBound;

        for (int i = 0; i < moves.Count; i++)
        {
            Move move = moves[i];

            // Send current move info at root for deeper searches
            if (ply == 0 && depth > 5 && i > 0 && _timer.ElapsedMilliseconds > 100)
            {
                SendCurrMoveInfo(move, i + 1);
            }

            Board newBoard = board;
            newBoard.MakeMove(move);

            int score;

            // Principal variation search
            if (i == 0)
            {
                score = -AlphaBeta(ref newBoard, depth - 1, -beta, -alpha, ply + 1, isPvNode);
            }
            else
            {
                // Late move reduction
                int reduction = 0;
                if (depth >= 3 && i >= 4 && !move.IsCapture && !move.IsPromotion && !board.IsInCheck())
                    reduction = 1;

                // Search with null window
                score = -AlphaBeta(ref newBoard, depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, false);

                // Re-search if it improves alpha
                if (score > alpha && (reduction > 0 || score < beta))
                    score = -AlphaBeta(ref newBoard, depth - 1, -beta, -alpha, ply + 1, isPvNode);
            }

            if (_stop)
                return 0;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;

                if (score > alpha)
                {
                    alpha = score;
                    flag = TTFlag.Exact;

                    // Update PV
                    UpdatePV(ply, move);

                    // Update iteration best at root
                    if (ply == 0)
                    {
                        _iterationBestMove = move;
                        _iterationBestScore = score;
                    }

                    if (score >= beta)
                    {
                        flag = TTFlag.LowerBound;

                        // Update killer moves and history
                        if (!move.IsCapture)
                            _moveOrdering.UpdateKillers(move, ply);

                        break;
                    }
                }
            }
        }

        // Store in transposition table
        int storeScore = bestScore;
        if (bestScore > MateScore - 100)
            storeScore += ply;
        else if (bestScore < -MateScore + 100)
            storeScore -= ply;

        _tt.Store(hash, depth, storeScore, flag, bestMove);

        return bestScore;
    }

    private void UpdatePV(int ply, Move move)
    {
        // Store the move at current ply
        _pvTable[ply, ply] = move;

        // Copy the PV from the next ply
        for (int i = ply + 1; i < _pvLength[ply + 1]; i++)
        {
            _pvTable[ply, i] = _pvTable[ply + 1, i];
        }

        // Update PV length
        _pvLength[ply] = _pvLength[ply + 1];
    }

    private int Quiescence(ref Board board, int alpha, int beta, int ply)
    {
        _nodes++;

        // Check time periodically in quiescence too
        if ((_nodes & 8191) == 0)
        {
            long currentTime = _timer.ElapsedMilliseconds;
            if (_timeAllocated != long.MaxValue && currentTime > _timeAllocated)
            {
                _stop = true;
                return 0;
            }
        }

        int standPat = Evaluation.Evaluate(ref board);

        if (standPat >= beta)
            return beta;

        if (standPat > alpha)
            alpha = standPat;

        // Generate only captures
        MoveList moves = new MoveList();
        MoveGenerator.GenerateMoves(ref board, ref moves);

        for (int i = 0; i < moves.Count; i++)
        {
            Move move = moves[i];

            // Only search captures in quiescence
            if (!move.IsCapture)
                continue;

            Board newBoard = board;
            newBoard.MakeMove(move);

            int score = -Quiescence(ref newBoard, -beta, -alpha, ply + 1);

            if (_stop)
                return 0;

            if (score >= beta)
                return beta;

            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    private bool IsRepetition(ref Board board)
    {
        // TODO: Implement proper repetition detection
        return false;
    }

    private string GetPvString()
    {
        System.Text.StringBuilder pv = new System.Text.StringBuilder();

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
    }
}