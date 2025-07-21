namespace Chess;

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

public class Search
{
    private const int MaxDepth = 64;
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

    // Principal variation
    private readonly Move[,] _pvTable = new Move[MaxDepth, MaxDepth];
    private readonly int[] _pvLength = new int[MaxDepth];

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

        Move bestMove = default;
        int bestScore = -Infinity;

        // Iterative deepening
        for (int depth = 1; depth <= maxDepth && !_stop; depth++)
        {
            int score = AlphaBeta(ref board, depth, -Infinity, Infinity, 0, true);

            if (!_stop)
            {
                bestScore = score;
                if (_pvLength[0] > 0)
                    bestMove = _pvTable[0, 0];

                // UCI info output
                long time = _timer.ElapsedMilliseconds;
                long nps = time > 0 ? (_nodes * 1000) / time : 0;

                Console.WriteLine($"info depth {depth} seldepth {_selDepth} score cp {bestScore} " +
                                $"nodes {_nodes} nps {nps} time {time} pv {GetPvString()}");

                // Time management - stop if we've used enough time
                if (time > _timeAllocated / 3)
                    break;
            }
        }

        return bestMove;
    }

    private int AlphaBeta(ref Board board, int depth, int alpha, int beta, int ply, bool isPvNode)
    {
        // Check time
        if ((_nodes & 2047) == 0 && _timer.ElapsedMilliseconds > _timeAllocated)
        {
            _stop = true;
            return 0;
        }

        _pvLength[ply] = ply;

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
                    _pvTable[ply, ply] = move;
                    Array.Copy(_pvTable, (ply + 1) * MaxDepth, _pvTable, ply * MaxDepth + 1, _pvLength[ply + 1] - ply - 1);
                    _pvLength[ply] = _pvLength[ply + 1];

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

    private int Quiescence(ref Board board, int alpha, int beta, int ply)
    {
        _nodes++;

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

            if (score >= beta)
                return beta;

            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    private bool IsRepetition(ref Board board)
    {
        // Simplified repetition detection
        // In a real implementation, you'd track position history
        return false;
    }

    private string GetPvString()
    {
        string pv = "";
        for (int i = 0; i < _pvLength[0]; i++)
        {
            if (i > 0) pv += " ";
            pv += _pvTable[0, i].ToString();
        }
        return pv;
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

// Move ordering for better alpha-beta pruning
public class MoveOrdering
{
    private readonly Move[,] _killers = new Move[64, 2];
    private readonly int[,] _history = new int[64, 64];

    public void OrderMoves(ref Board board, ref MoveList moves, Move ttMove, int ply)
    {
        // Create a temporary array for sorting
        Move[] moveArray = new Move[moves.Count];
        int[] scores = new int[moves.Count];

        // Copy moves and calculate scores
        for (int i = 0; i < moves.Count; i++)
        {
            moveArray[i] = moves[i];
            Move move = moveArray[i];

            // TT move gets highest priority
            if (move == ttMove)
                scores[i] = 1000000;
            // Captures - use MVV-LVA
            else if (move.IsCapture)
            {
                var (capturedPiece, _) = board.GetPieceAt(move.To);
                var (movingPiece, _) = board.GetPieceAt(move.From);
                scores[i] = 10000 + GetPieceValue(capturedPiece) - GetPieceValue(movingPiece);
            }
            // Promotions
            else if (move.IsPromotion)
                scores[i] = 9000 + (int)move.Promotion * 100;
            // Killer moves
            else if (move == _killers[ply, 0])
                scores[i] = 8000;
            else if (move == _killers[ply, 1])
                scores[i] = 7000;
            // History heuristic
            else
                scores[i] = _history[move.From, move.To];
        }

        // Selection sort
        for (int i = 0; i < moves.Count - 1; i++)
        {
            int bestIdx = i;
            int bestScore = scores[i];

            for (int j = i + 1; j < moves.Count; j++)
            {
                if (scores[j] > bestScore)
                {
                    bestScore = scores[j];
                    bestIdx = j;
                }
            }

            if (bestIdx != i)
            {
                // Swap moves and scores
                Move tempMove = moveArray[i];
                moveArray[i] = moveArray[bestIdx];
                moveArray[bestIdx] = tempMove;

                int tempScore = scores[i];
                scores[i] = scores[bestIdx];
                scores[bestIdx] = tempScore;
            }
        }

        // Copy sorted moves back
        moves.Clear();
        for (int i = 0; i < moveArray.Length; i++)
        {
            moves.Add(moveArray[i]);
        }
    }

    public void UpdateKillers(Move move, int ply)
    {
        if (move != _killers[ply, 0])
        {
            _killers[ply, 1] = _killers[ply, 0];
            _killers[ply, 0] = move;
        }
    }

    private int GetPieceValue(PieceType piece)
    {
        return piece switch
        {
            PieceType.Pawn => 100,
            PieceType.Knight => 320,
            PieceType.Bishop => 330,
            PieceType.Rook => 500,
            PieceType.Queen => 900,
            _ => 0
        };
    }
}