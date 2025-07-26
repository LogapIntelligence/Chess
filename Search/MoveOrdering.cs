using System;
using System.Runtime.CompilerServices;
using Move;

namespace Search
{
    public class MoveOrdering
    {
        // Move ordering scores
        private const int TT_MOVE_SCORE = 1000000;
        private const int GOOD_CAPTURE_SCORE = 900000;
        private const int KILLER_MOVE_1_SCORE = 800000;
        private const int KILLER_MOVE_2_SCORE = 790000;
        private const int BAD_CAPTURE_SCORE = -1000000;

        private readonly int[] moveScores = new int[256];

        public int OrderMoves(Move.Move[] moves, Move.Move ttMove, Move.Move killer1, Move.Move killer2,
                            int[,] historyTable, Position position)
        {
            var moveCount = moves.Length;

            // Score all moves
            for (int i = 0; i < moveCount; i++)
            {
                var move = moves[i];

                if (move == ttMove)
                {
                    moveScores[i] = TT_MOVE_SCORE;
                }
                else if (move.IsCapture)
                {
                    moveScores[i] = ScoreCapture(move, position);
                }
                else if (move == killer1)
                {
                    moveScores[i] = KILLER_MOVE_1_SCORE;
                }
                else if (move == killer2)
                {
                    moveScores[i] = KILLER_MOVE_2_SCORE;
                }
                else
                {
                    // History heuristic - cast Square to int
                    moveScores[i] = historyTable[(int)move.From, (int)move.To];
                }
            }

            // Selection sort first few moves
            for (int i = 0; i < Math.Min(moveCount, 4); i++)
            {
                var bestIdx = i;
                var bestScore = moveScores[i];

                for (int j = i + 1; j < moveCount; j++)
                {
                    if (moveScores[j] > bestScore)
                    {
                        bestScore = moveScores[j];
                        bestIdx = j;
                    }
                }

                if (bestIdx != i)
                {
                    // Swap moves and scores
                    (moves[i], moves[bestIdx]) = (moves[bestIdx], moves[i]);
                    (moveScores[i], moveScores[bestIdx]) = (moveScores[bestIdx], moveScores[i]);
                }
            }

            return moveCount;
        }

        public int OrderCaptures(Move.Move[] moves, Position position)
        {
            var moveCount = moves.Length;

            // Score captures using MVV-LVA
            for (int i = 0; i < moveCount; i++)
            {
                moveScores[i] = ScoreCapture(moves[i], position);
            }

            // Sort by score
            Array.Sort(moveScores, moves, 0, moveCount);
            Array.Reverse(moves, 0, moveCount);

            return moveCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ScoreCapture(Move.Move move, Position position)
        {
            var captured = position.At(move.To);
            var attacker = position.At(move.From);

            if (captured == Piece.NoPiece)
                return 0;

            // MVV-LVA (Most Valuable Victim - Least Valuable Attacker)
            var victimValue = GetPieceValue(Types.TypeOf(captured));
            var attackerValue = GetPieceValue(Types.TypeOf(attacker));

            // SEE (Static Exchange Evaluation) approximation
            if (victimValue >= attackerValue)
            {
                return GOOD_CAPTURE_SCORE + victimValue - attackerValue;
            }
            else
            {
                // Bad capture - losing material
                return BAD_CAPTURE_SCORE + victimValue - attackerValue;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetPieceValue(PieceType pt)
        {
            return pt switch
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
    }
}