namespace Chess;

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

/// <summary>
/// Implements the search algorithm for the chess engine.
/// This class uses iterative deepening, alpha-beta pruning, and a variety of
/// advanced search techniques like transposition tables, null move pruning,
/// futility pruning, and late move reductions to efficiently find the best move.
/// </summary>
public class Search
{
    // ## Constants ##

    /// <summary>
    /// The absolute maximum search depth in plies (half-moves).
    /// </summary>
    public const int MaxDepth = 64;

    /// <summary>
    /// A value representing infinity, used for alpha-beta bounds. It should be
    /// higher than any possible evaluation score.
    /// </summary>
    private const int Infinity = 1000000;

    /// <summary>
    /// The base score for a checkmate. A checkmate found at a deeper ply will have a
    /// slightly lower score, encouraging the engine to find faster mates.
    /// </summary>
    private const int MateScore = 100000;

    /// <summary>
    /// The score for a drawn position (e.g., stalemate, threefold repetition).
    /// </summary>
    private const int DrawScore = 0;

    // ## Core Components ##

    /// <summary>
    /// The transposition table (TT) stores previously calculated search results,
    /// significantly speeding up the search by avoiding re-computation of the same positions.
    /// </summary>
    private readonly TranspositionTable _tt;

    /// <summary>
    /// Manages move ordering heuristics (e.g., killer moves, history heuristic)
    /// to prioritize moves that are likely to be good. Effective move ordering is
    /// crucial for the efficiency of the alpha-beta algorithm.
    /// </summary>
    private readonly MoveOrdering _moveOrdering;

    /// <summary>
    /// A stopwatch to keep track of the time spent on searching.
    /// </summary>
    private readonly Stopwatch _timer;

    // ## Search State Variables ##

    /// <summary>
    /// The total number of nodes (positions) visited during the current search.
    /// </summary>
    private long _nodes;

    /// <summary>
    /// The total time in milliseconds allocated for the current search.
    /// </summary>
    private long _timeAllocated;

    /// <summary>
    /// A flag that, when set to true, signals the search to stop immediately.
    /// </summary>
    private bool _stop;

    /// <summary>
    /// The maximum search depth reached, including the quiescence search.
    /// </summary>
    private int _selDepth;

    // ## Optimizations & UCI Communication ##

    /// <summary>
    /// To avoid the overhead of checking the time on every single node, we only check it
    /// periodically. This mask is used to check the time every 16,384 nodes (0x3FFF + 1).
    /// `(_nodes & TimeCheckMask) == 0`
    /// </summary>
    private const int TimeCheckMask = 0x3FFF;

    /// <summary>
    /// The last time an "info" string was sent to the GUI. Used for periodic updates.
    /// </summary>
    private long _lastInfoTime;

    /// <summary>
    /// The interval in milliseconds for sending periodic "info" updates.
    /// </summary>
    private const long InfoInterval = 1000;

    // ## Principal Variation (PV) Data Structures ##

    /// <summary>
    /// The Principal Variation (PV) table, often called a "triangular" PV table.
    /// _pvTable[ply, 0] stores the best move at a given ply.
    /// _pvTable[ply, 1] stores the best response, and so on.
    /// This table is constructed during the search to store the sequence of best moves.
    /// </summary>
    private readonly Move[,] _pvTable = new Move[MaxDepth, MaxDepth];

    /// <summary>
    /// Stores the length of the principal variation at each ply.
    /// _pvLength[ply] tells us how many moves are in the PV starting from that ply.
    /// </summary>
    private readonly int[] _pvLength = new int[MaxDepth];

    // ## Best Move Tracking ##

    /// <summary>
    /// The best move found at the root of the search for the current iterative deepening depth.
    /// </summary>
    private Move _rootBestMove;

    /// <summary>
    /// The score corresponding to the _rootBestMove.
    /// </summary>
    private int _rootBestScore;

    /// <summary>
    /// Stores the transposition table usage percentage (permille, i.e., per 1000).
    /// </summary>
    private int _hashfull = 0;

    // ## Repetition Detection ##

    /// <summary>
    /// A history of Zobrist hashes for all positions played in the current search path.
    /// Used to detect threefold repetitions.
    /// </summary>
    private readonly ulong[] _hashHistory = new ulong[16000];
    private int _hashHistoryCount = 0;

    // ## Pre-allocated Memory ##

    /// <summary>
    /// Pre-allocated move lists for each ply to avoid repeated memory allocation
    /// and reduce garbage collection pressure during the search.
    /// </summary>
    private readonly MoveList[] _moveLists = new MoveList[MaxDepth];

    /// <summary>
    /// Margins for futility pruning, indexed by depth. If the static evaluation plus
    /// this margin is still below alpha, we can prune the move.
    /// </summary>
    private static readonly int[] FutilityMargins = { 0, 100, 200, 300, 400, 500 };

    /// <summary>
    /// Initializes a new instance of the Search class.
    /// </summary>
    /// <param name="ttSizeMb">The size of the transposition table in megabytes.</param>
    public Search(int ttSizeMb = 128)
    {
        _tt = new TranspositionTable(ttSizeMb);
        _moveOrdering = new MoveOrdering();
        _timer = new Stopwatch();
    }

    /// <summary>
    /// The main entry point for starting a search.
    /// </summary>
    /// <param name="board">The current board state.</param>
    /// <param name="timeMs">The time allocated for this search in milliseconds.</param>
    /// <param name="maxDepth">The maximum depth to search to.</param>
    /// <returns>The best move found.</returns>
    public Move Think(ref Board board, long timeMs, int maxDepth = MaxDepth)
    {
        // --- 1. Initialization ---
        _timer.Restart();
        _timeAllocated = timeMs;
        _stop = false;
        _nodes = 0;
        _selDepth = 0;
        _lastInfoTime = 0;
        _hashfull = 0;
        _hashHistoryCount = 0;

        // Add the starting position hash to the history for repetition detection.
        _hashHistory[_hashHistoryCount++] = board.GetZobristHash();

        _rootBestMove = default;
        _rootBestScore = -Infinity;

        // --- 2. Check for Legal Moves ---
        MoveList initialMoves = new MoveList();
        MoveGenerator.GenerateLegalMoves(ref board, ref initialMoves);

        // If no legal moves, it's either checkmate or stalemate.
        // Return a null move and let the GUI handle it.
        if (initialMoves.Count == 0)
        {
            Console.WriteLine("bestmove 0000");
            Console.Out.Flush();
            return default;
        }

        // Always have a valid move to fall back on, just in case the search is stopped early.
        _rootBestMove = initialMoves[0];

        // --- 3. Iterative Deepening Loop ---
        // The search starts at depth 1 and incrementally increases the depth.
        // This allows the engine to stop at any time and return the best move found
        // so far. It also seeds the transposition table and move ordering heuristics
        // for deeper searches.
        for (int depth = 1; depth <= maxDepth && !_stop; depth++)
        {
            _selDepth = 0;

            // Clear PV from the previous iteration.
            Array.Clear(_pvLength, 0, _pvLength.Length);
            Array.Clear(_pvTable, 0, _pvTable.Length);

            // --- 4. Aspiration Windows ---
            // For deeper searches, we assume the score will be close to the score from
            // the previous iteration. We search a narrow window [alpha, beta] around
            // the previous score (_rootBestScore).
            int alpha = -Infinity;
            int beta = Infinity;
            int aspirationDelta = 50; // Initial window size (e.g., half a pawn)

            if (depth >= 4 && Math.Abs(_rootBestScore) < MateScore - 1000)
            {
                alpha = _rootBestScore - aspirationDelta;
                beta = _rootBestScore + aspirationDelta;
            }

            // --- 5. Aspiration Window Search Loop ---
            // If the search fails (returns a score outside the window), we widen
            // the window and search again at the same depth.
            while (true)
            {
                int score = AlphaBeta(ref board, depth, alpha, beta, 0, true);

                if (_stop) break;

                // Handle aspiration window failures:
                if (score <= alpha) // Fail-low: the score was worse than we expected.
                {
                    // Widen the window downwards and try again.
                    alpha = Math.Max(alpha - aspirationDelta, -Infinity);
                    aspirationDelta = Math.Min(aspirationDelta * 2, 500); // Increase delta
                }
                else if (score >= beta) // Fail-high: the score was better than we expected.
                {
                    // Widen the window upwards and try again.
                    beta = Math.Min(beta + aspirationDelta, Infinity);
                    aspirationDelta = Math.Min(aspirationDelta * 2, 500); // Increase delta
                }
                else // Success: the score was within the [alpha, beta] window.
                {
                    _rootBestScore = score;

                    // The best move is the first move in the Principal Variation.
                    if (_pvLength[0] > 0 && _pvTable[0, 0] != default)
                    {
                        _rootBestMove = _pvTable[0, 0];
                    }

                    break; // Exit the aspiration loop and move to the next depth.
                }
            }

            // --- 6. Send Info to GUI ---
            if (!_stop)
            {
                // Send a UCI "info" string with details about the search at this depth.
                SendInfo(depth, _selDepth, _rootBestScore, GetPvString());
            }

            // --- 7. Time Management ---
            if (_timeAllocated != long.MaxValue)
            {
                long elapsed = _timer.ElapsedMilliseconds;

                // Simple time management: if a significant portion of time is used,
                // stop early to avoid forfeiting on time.
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

        // --- 8. Output Best Move ---
        // Send the final "bestmove" command to the GUI.
        Console.WriteLine($"bestmove {_rootBestMove}");
        Console.Out.Flush();

        return _rootBestMove;
    }

    /// <summary>
    /// The core recursive alpha-beta search function.
    /// </summary>
    /// <param name="board">The current board state.</param>
    /// <param name="depth">The remaining depth to search.</param>
    /// <param name="alpha">The lower bound of the search window (best score for maximizing player).</param>
    /// <param name="beta">The upper bound of the search window (best score for minimizing player).</param>
    /// <param name="ply">The current search depth from the root.</param>
    /// <param name="isPvNode">True if this node is part of the Principal Variation.</param>
    /// <returns>The evaluation of the position.</returns>
    private int AlphaBeta(ref Board board, int depth, int alpha, int beta, int ply, bool isPvNode)
    {
        // --- 1. Initialization and Termination Checks ---
        _pvLength[ply] = ply; // Initialize PV length for this ply.

        // Check for time up, but only periodically to reduce overhead.
        if ((_nodes & TimeCheckMask) == 0)
        {
            long currentTime = _timer.ElapsedMilliseconds;
            if (_timeAllocated != long.MaxValue && currentTime > _timeAllocated)
            {
                _stop = true;
                return 0; // Return a neutral score if stopped.
            }
            // Send periodic info for very long searches at the root.
            if (ply == 0 && currentTime - _lastInfoTime >= InfoInterval)
            {
                SendPeriodicInfo();
            }
        }

        // Check for draws (50-move rule, threefold repetition).
        if (ply > 0)
        {
            if (board.HalfmoveClock >= 100) return DrawScore;

            // Check if the current position has been repeated.
            ulong currentHash = board.GetZobristHash();
            int repCount = 0;
            // Search backwards through history, but no further than the last irreversible move.
            for (int i = _hashHistoryCount - 2; i >= 0 && i >= _hashHistoryCount - board.HalfmoveClock; i -= 2)
            {
                if (_hashHistory[i] == currentHash)
                {
                    repCount++;
                    // A 3-fold repetition is a draw. We return draw on the 2nd repetition.
                    if (repCount >= 2) return DrawScore;
                }
            }
        }

        // Mate Distance Pruning: If we've already found a mate, we can't get a better score.
        // This also helps find the shortest mate.
        int matedScore = -MateScore + ply; // Score if we are getting mated.
        int mateScore = MateScore - ply - 1; // Score if we are delivering mate.
        if (matedScore >= beta) return beta;
        if (mateScore <= alpha) return alpha;
        alpha = Math.Max(alpha, matedScore);
        beta = Math.Min(beta, mateScore);


        // --- 2. Transposition Table Probe ---
        ulong hash = board.GetZobristHash();
        var ttEntry = _tt.Probe(hash);
        Move ttMove = default;

        // If we get a TT hit with sufficient depth, we may be able to use the score directly.
        if (ttEntry.MatchesHash(hash) && ttEntry.Depth >= depth && !isPvNode)
        {
            ttMove = ttEntry.GetMove();
            int ttScore = ttEntry.Score;

            // Adjust mate scores from TT to be relative to the current ply.
            if (ttScore > MateScore - 100) ttScore -= ply;
            else if (ttScore < -MateScore + 100) ttScore += ply;

            TTFlag eflag = ttEntry.GetFlag();
            // Exact score: The stored score is the true value of the node.
            if (eflag == TTFlag.Exact) return ttScore;
            // Lower bound: The true score is at least ttScore.
            else if (eflag == TTFlag.LowerBound) alpha = Math.Max(alpha, ttScore);
            // Upper bound: The true score is at most ttScore.
            else if (eflag == TTFlag.UpperBound) beta = Math.Min(beta, ttScore);

            // If the TT entry causes a beta-cutoff, we can return immediately.
            if (alpha >= beta) return ttScore;
        }

        // Even if the depth was insufficient, the move from the TT is still likely the best.
        if (ttEntry.MatchesHash(hash))
        {
            ttMove = ttEntry.GetMove();
        }

        // --- 3. Leaf Node Evaluation / Quiescence Search ---
        if (depth <= 0)
        {
            // When we reach max depth, drop into Quiescence search to evaluate only
            // tactical moves (captures/promotions) to ensure we are evaluating a "quiet" position.
            return Quiescence(ref board, alpha, beta, ply);
        }

        _nodes++;
        if (ply > _selDepth) _selDepth = ply;

        bool inCheck = board.IsInCheckFast();
        int eval = inCheck ? -Infinity : Evaluation.Evaluate(ref board); // Get a static eval unless in check.

        // --- 4. Search Pruning Techniques ---

        // Reverse Futility Pruning (Static Null Move Pruning)
        // If static evaluation is very high, it's unlikely a move will lower the score below beta.
        if (!isPvNode && !inCheck && depth <= 6 && eval - FutilityMargins[depth] >= beta)
        {
            return eval; // Prune the search at this node.
        }

        // Null Move Pruning (NMP)
        // If we can give the opponent a free move and their position is still bad, our position must be very good.
        if (!isPvNode && !inCheck && depth >= 3 && ply > 0 && board.HasNonPawnMaterial() && eval >= beta)
        {
            // R is the reduction factor, typically 2 or 3.
            int R = 3 + depth / 6 + Math.Min((eval - beta) / 200, 3);

            // Make a "null" move.
            Board nullBoard = board;
            nullBoard.SideToMove ^= (Color)1;
            nullBoard.EnPassantSquare = -1;
            nullBoard.HalfmoveClock++;
            if (nullBoard.SideToMove == Color.Black) nullBoard.FullmoveNumber++;

            // Search with a reduced depth and a null window.
            int nullScore = -AlphaBeta(ref nullBoard, depth - R - 1, -beta, -beta + 1, ply + 1, false);

            if (_stop) return 0;

            // If the null move search fails high, it means our position is very strong,
            // and we can likely prune this node.
            if (nullScore >= beta)
            {
                // For very deep searches, a verification search is sometimes done to ensure stability.
                if (depth > 12)
                {
                    int verifyScore = AlphaBeta(ref board, depth - R, beta - 1, beta, ply, false);
                    if (verifyScore >= beta) return beta;
                }
                else
                {
                    return beta;
                }
            }
        }

        // --- 5. Move Generation and Search Loop ---
        MoveList moves = new();
        MoveGenerator.GenerateLegalMoves(ref board, ref moves);

        // If no moves are generated, it's either checkmate or stalemate.
        if (moves.Count == 0)
        {
            return inCheck ? -MateScore + ply : DrawScore;
        }

        // Order moves to try the most promising ones first, increasing beta-cutoffs.
        _moveOrdering.OrderMoves(ref board, ref moves, ttMove, ply);

        Move bestMove = default;
        int bestScore = -Infinity;
        TTFlag flag = TTFlag.UpperBound; // Assume we won't raise alpha.
        int movesSearched = 0;

        for (int i = 0; i < moves.Count; i++)
        {
            Move move = moves[i];
            // Send UCI info about the current move being searched.
            if (ply == 0 && depth > 5 && movesSearched > 0 && _timer.ElapsedMilliseconds > 500)
            {
                SendCurrMoveInfo(move, movesSearched + 1);
            }

            // Futility Pruning
            // If we are deep into the search (many moves searched) and the current move is
            // not tactical, and our static eval is poor, it's unlikely this move will raise alpha.
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

            // --- 6. Principal Variation Search (PVS) ---
            if (movesSearched == 0) // The first move is assumed to be the best (PV move).
            {
                // Search it with a full [alpha, beta] window.
                score = -AlphaBeta(ref newBoard, depth - 1, -beta, -alpha, ply + 1, isPvNode);
            }
            else // For subsequent moves, we use PVS.
            {
                int reduction = 0;

                // Late Move Reduction (LMR): reduce the search depth for moves that appear late in the list.
                if (depth >= 3 && movesSearched >= 3 && !move.IsCapture &&
                    !move.IsPromotion && !inCheck && !newBoard.IsInCheckFast())
                {
                    reduction = 1;
                    if (movesSearched >= 6) reduction++;
                    if (depth >= 6 && movesSearched >= 10) reduction++;
                    reduction = Math.Min(reduction, depth - 2);
                }

                // Search with a "null window" [-alpha-1, -alpha]. This is faster than a full window search
                // and is used to quickly check if a move is better than the current best.
                score = -AlphaBeta(ref newBoard, depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, false);

                // If the null-window search returned a score better than alpha, this move is
                // better than expected. We must re-search it with the full window.
                if (score > alpha && (reduction > 0 || score < beta))
                {
                    score = -AlphaBeta(ref newBoard, depth - 1, -beta, -alpha, ply + 1, isPvNode);
                }
            }

            // Backtrack
            _hashHistoryCount--;
            movesSearched++;

            if (_stop) return 0;

            // --- 7. Update Best Move and Alpha/Beta ---
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;

                if (score > alpha)
                {
                    alpha = score;
                    flag = TTFlag.Exact; // We found a new best move, so it's a PV-node.

                    UpdatePV(ply, move);

                    // At the root, update the best move immediately.
                    if (ply == 0)
                    {
                        _rootBestMove = move;
                        _rootBestScore = score;
                    }

                    if (score >= beta) // Beta-cutoff
                    {
                        flag = TTFlag.LowerBound; // This move caused a cutoff, so the score is a lower bound.

                        // A quiet move that causes a cutoff is a "killer move".
                        if (!move.IsCapture)
                        {
                            _moveOrdering.UpdateKillers(move, ply);
                            _moveOrdering.UpdateHistory(move, depth);
                        }

                        break; // Stop searching other moves at this node.
                    }
                }
            }
        }

        // --- 8. Store Result in Transposition Table ---
        // Adjust mate scores to be absolute (not relative to ply) for storage.
        int storeScore = bestScore;
        if (bestScore > MateScore - 100) storeScore += ply;
        else if (bestScore < -MateScore + 100) storeScore -= ply;

        _tt.Store(hash, depth, storeScore, flag, bestMove);

        return bestScore;
    }

    /// <summary>
    /// A specialized search that only evaluates tactical moves (captures and promotions)
    /// to ensure the final evaluation is not based on a position in the middle of a
    /// tactical sequence (solving the "horizon effect").
    /// </summary>
    private int Quiescence(ref Board board, int alpha, int beta, int ply)
    {
        _nodes++;

        // Periodically check if time is up.
        if ((_nodes & 0x1FFF) == 0 && _timeAllocated != long.MaxValue &&
            _timer.ElapsedMilliseconds > _timeAllocated)
        {
            _stop = true;
            return 0;
        }

        // The "stand-pat" score: the evaluation of the position without making any further moves.
        int standPat = Evaluation.Evaluate(ref board);

        // If the stand-pat score is already >= beta, the opponent won't allow this position,
        // so we can prune.
        if (standPat >= beta) return beta;

        // Delta Pruning: If the stand-pat score plus a large margin (e.g., a queen)
        // is still less than alpha, it's highly unlikely any capture will raise the score enough.
        const int BigDelta = 900; // Queen value
        if (standPat + BigDelta < alpha) return alpha;

        // If the stand-pat score is better than our current alpha, update alpha.
        if (standPat > alpha) alpha = standPat;

        // Generate moves and filter for only captures and promotions.
        MoveList moves = new();
        MoveGenerator.GenerateMoves(ref board, ref moves);

        // Score captures to search the best ones first (e.g., using MVV-LVA).
        // (Note: This implementation simply iterates, a full implementation would score/sort here).
        for (int i = 0; i < moves.Count; i++)
        {
            Move move = moves[i];

            // Only consider captures and promotions in quiescence search.
            if (!move.IsCapture && !move.IsPromotion) continue;

            Board newBoard = board;
            newBoard.MakeMove(move);

            int score = -Quiescence(ref newBoard, -beta, -alpha, ply + 1);

            if (_stop) return 0;

            // Standard alpha-beta updates.
            if (score >= beta) return beta; // Beta-cutoff.
            if (score > alpha) alpha = score;
        }

        return alpha;
    }

    /// <summary>
    /// Updates the Principal Variation (PV) table.
    /// This is done by copying the PV from the child node up to the current node's PV.
    /// </summary>
    private void UpdatePV(int ply, Move move)
    {
        // The first move of the PV at this ply is the new best move.
        _pvTable[ply, ply] = move;

        // Copy the rest of the PV from the child node (at ply + 1).
        for (int i = ply + 1; i < _pvLength[ply + 1]; i++)
        {
            _pvTable[ply, i] = _pvTable[ply + 1, i];
        }

        // Update the PV length for the current ply.
        _pvLength[ply] = _pvLength[ply + 1];
    }

    // ###################################
    // ## UCI Communication Helpers ##
    // ###################################

    private void SendInfo(int depth, int selDepth, int score, string pv)
    {
        long time = _timer.ElapsedMilliseconds;
        long nps = time > 0 ? (_nodes * 1000) / time : 0;
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
        long nps = time > 0 ? (_nodes * 1000) / time : 0;
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
            // Calculate moves to mate. The +1 is for rounding.
            int mateIn = (MateScore - Math.Abs(score) + 1) / 2;
            return score > 0 ? $"mate {mateIn}" : $"mate -{mateIn}";
        }
        return $"cp {score}"; // Score in centipawns.
    }

    private string GetPvString()
    {
        var pv = new System.Text.StringBuilder();
        // The root PV is at _pvTable[0].
        for (int i = 0; i < _pvLength[0]; i++)
        {
            Move move = _pvTable[0, i];
            if (move == default) break;
            if (i > 0) pv.Append(" ");
            pv.Append(move.ToString());
        }
        return pv.ToString();
    }

    /// <summary>
    /// Public method to stop the search, typically called from another thread.
    /// </summary>
    public void Stop()
    {
        _stop = true;
    }

    /// <summary>
    /// Clears hash-dependent tables. Called when the engine receives a "ucinewgame" command.
    /// </summary>
    public void ClearHash()
    {
        _tt.Clear();
        _moveOrdering.ClearHistory();
        _moveOrdering.ClearKillers();
    }
}