namespace Chess;

public class MoveOrdering
{
    private readonly Move[,] _killers = new Move[64, 2];
    private readonly int[,] _history = new int[64, 64];

    // Score constants for move ordering
    private const int TT_MOVE_SCORE = 1000000;
    private const int GOOD_CAPTURE_SCORE = 100000;
    private const int PROMOTION_SCORE = 90000;
    private const int KILLER_1_SCORE = 80000;
    private const int KILLER_2_SCORE = 70000;
    private const int BAD_CAPTURE_SCORE = -100000;

    public void OrderMoves(ref Board board, ref MoveList moves, Move ttMove, int ply)
    {
        if (moves.Count <= 1) return;

        // Create arrays for sorting
        Move[] moveArray = new Move[moves.Count];
        int[] scores = new int[moves.Count];

        // Calculate scores for each move
        for (int i = 0; i < moves.Count; i++)
        {
            moveArray[i] = moves[i];
            scores[i] = ScoreMove(ref board, moveArray[i], ttMove, ply);
        }

        // Sort moves by score (descending)
        QuickSort(moveArray, scores, 0, moveArray.Length - 1);

        // Copy sorted moves back
        moves.Clear();
        for (int i = 0; i < moveArray.Length; i++)
        {
            moves.Add(moveArray[i]);
        }
    }

    private int ScoreMove(ref Board board, Move move, Move ttMove, int ply)
    {
        // TT move gets highest priority
        if (move.Equals(ttMove))
            return TT_MOVE_SCORE;

        // Score captures using MVV-LVA
        if (move.IsCapture)
        {
            var (capturedPiece, _) = board.GetPieceAt(move.To);
            var (movingPiece, _) = board.GetPieceAt(move.From);

            int capturedValue = GetPieceValue(capturedPiece);
            int attackerValue = GetPieceValue(movingPiece);

            // Good captures (capturing more valuable piece with less valuable)
            if (capturedValue >= attackerValue)
            {
                return GOOD_CAPTURE_SCORE + capturedValue - attackerValue;
            }
            else
            {
                // Bad captures get negative score
                return BAD_CAPTURE_SCORE + capturedValue - attackerValue;
            }
        }

        // Promotions
        if (move.IsPromotion)
        {
            int promoScore = move.Promotion switch
            {
                PieceType.Queen => 900,
                PieceType.Rook => 500,
                PieceType.Bishop => 330,
                PieceType.Knight => 320,
                _ => 0
            };
            return PROMOTION_SCORE + promoScore;
        }

        // Killer moves
        if (ply < 64)
        {
            if (move.Equals(_killers[ply, 0]))
                return KILLER_1_SCORE;
            if (move.Equals(_killers[ply, 1]))
                return KILLER_2_SCORE;
        }

        // History heuristic for quiet moves
        return _history[move.From, move.To];
    }

    private void QuickSort(Move[] moves, int[] scores, int left, int right)
    {
        if (left < right)
        {
            int pivot = Partition(moves, scores, left, right);
            QuickSort(moves, scores, left, pivot - 1);
            QuickSort(moves, scores, pivot + 1, right);
        }
    }

    private int Partition(Move[] moves, int[] scores, int left, int right)
    {
        int pivotScore = scores[right];
        int i = left - 1;

        for (int j = left; j < right; j++)
        {
            // Sort in descending order
            if (scores[j] > pivotScore)
            {
                i++;
                Swap(moves, scores, i, j);
            }
        }

        Swap(moves, scores, i + 1, right);
        return i + 1;
    }

    private void Swap(Move[] moves, int[] scores, int i, int j)
    {
        Move tempMove = moves[i];
        moves[i] = moves[j];
        moves[j] = tempMove;

        int tempScore = scores[i];
        scores[i] = scores[j];
        scores[j] = tempScore;
    }

    public void UpdateKillers(Move move, int ply)
    {
        try
        {


            if (ply >= 64) return;

            // Don't store captures as killers
            if (move.IsCapture) return;

            if (!move.Equals(_killers[ply, 0]))
            {
                _killers[ply, 1] = _killers[ply, 0];
                _killers[ply, 0] = move;
            }
        }
        catch (Exception e)
        {

        }
    }

    public void UpdateHistory(Move move, int depth)
    {
        try
        {


            if (!move.IsCapture && move.From >= 0 && move.From < 64 && move.To >= 0 && move.To < 64)
            {
                _history[move.From, move.To] += depth * depth;

                // Prevent overflow
                if (_history[move.From, move.To] > 100000)
                {
                    // Scale down all history values
                    for (int i = 0; i < 64; i++)
                    {
                        for (int j = 0; j < 64; j++)
                        {
                            _history[i, j] /= 2;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {

        }
    }

    public void ClearKillers()
    {
        for (int i = 0; i < 64; i++)
        {
            _killers[i, 0] = default;
            _killers[i, 1] = default;
        }
    }

    public void ClearHistory()
    {
        Array.Clear(_history, 0, _history.Length);
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
            PieceType.King => 20000,
            _ => 0
        };
    }
}