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
        private const int EQUAL_CAPTURE_SCORE = 850000;
        private const int KILLER_MOVE_1_SCORE = 800000;
        private const int KILLER_MOVE_2_SCORE = 790000;
        private const int COUNTER_MOVE_SCORE = 780000;
        private const int BAD_CAPTURE_SCORE = -1000000;

        private readonly int[] moveScores = new int[256];

        public int OrderMoves(Move.Move[] moves, Move.Move ttMove, Move.Move killer1, Move.Move killer2,
                            int[,] historyTable, Position position, Move.Move counterMove = default)
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
                else if (move == counterMove && counterMove.From != counterMove.To)
                {
                    moveScores[i] = COUNTER_MOVE_SCORE;
                }
                else
                {
                    // History heuristic with better scaling
                    moveScores[i] = historyTable[(int)move.From, (int)move.To];

                    // Bonus for advancing pieces toward center
                    moveScores[i] += PieceSquareBonus(move, position);
                }
            }

            // Selection sort first few moves for better move ordering
            int sortLimit = Math.Min(moveCount, 8);
            for (int i = 0; i < sortLimit; i++)
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

        // Overload that accepts moveCount parameter for when using pre-allocated arrays
        public int OrderMoves(Move.Move[] moves, int moveCount, Move.Move ttMove, Move.Move killer1, Move.Move killer2,
                            int[,] historyTable, Position position, Move.Move counterMove = default)
        {
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
                else if (move == counterMove && counterMove.From != counterMove.To)
                {
                    moveScores[i] = COUNTER_MOVE_SCORE;
                }
                else
                {
                    // History heuristic with better scaling
                    moveScores[i] = historyTable[(int)move.From, (int)move.To];

                    // Bonus for advancing pieces toward center
                    moveScores[i] += PieceSquareBonus(move, position);
                }
            }

            // Selection sort first few moves for better move ordering
            int sortLimit = Math.Min(moveCount, 8);
            for (int i = 0; i < sortLimit; i++)
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

            // Score captures using MVV-LVA with SEE
            for (int i = 0; i < moveCount; i++)
            {
                moveScores[i] = ScoreCapture(moves[i], position);
            }

            // Full sort for captures since they're usually fewer
            Array.Sort(moveScores, moves, 0, moveCount);
            Array.Reverse(moves, 0, moveCount);

            return moveCount;
        }

        // Overload that accepts moveCount parameter for when using pre-allocated arrays
        public int OrderCaptures(Move.Move[] moves, int moveCount, Position position)
        {
            // Score captures using MVV-LVA with SEE
            for (int i = 0; i < moveCount; i++)
            {
                moveScores[i] = ScoreCapture(moves[i], position);
            }

            // Full sort for captures since they're usually fewer
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
            {
                // Handle en passant
                if (move.Flags == MoveFlags.EnPassant)
                    return GOOD_CAPTURE_SCORE + 100; // Pawn value

                // Promotions
                if ((move.Flags & MoveFlags.Promotions) != 0)
                {
                    return move.Flags switch
                    {
                        MoveFlags.PrQueen => GOOD_CAPTURE_SCORE + 900,
                        MoveFlags.PrRook => GOOD_CAPTURE_SCORE + 500,
                        MoveFlags.PrBishop => GOOD_CAPTURE_SCORE + 330,
                        MoveFlags.PrKnight => GOOD_CAPTURE_SCORE + 320,
                        _ => 0
                    };
                }

                return 0;
            }

            // MVV-LVA (Most Valuable Victim - Least Valuable Attacker)
            var victimValue = GetPieceValue(Types.TypeOf(captured));
            var attackerValue = GetPieceValue(Types.TypeOf(attacker));

            // Simple SEE approximation
            if (victimValue >= attackerValue)
            {
                return GOOD_CAPTURE_SCORE + victimValue * 10 - attackerValue;
            }
            else
            {
                // Check if the capture is defended
                var defendersBb = GetDefenders(position, move.To);
                if (defendersBb != 0)
                {
                    // Bad capture - losing material
                    return BAD_CAPTURE_SCORE + victimValue - attackerValue;
                }
                else
                {
                    // Equal capture - victim is not defended
                    return EQUAL_CAPTURE_SCORE + victimValue * 10 - attackerValue;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong GetDefenders(Position position, Square square)
        {
            // Simple check for defenders - this is a basic approximation
            var occupancy = position.AllPieces(Color.White) | position.AllPieces(Color.Black);
            var enemyColor = Types.ColorOf(position.At(square));

            return position.AttackersFrom(enemyColor, square, occupancy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int PieceSquareBonus(Move.Move move, Position position)
        {
            var piece = position.At(move.From);
            if (piece == Piece.NoPiece) return 0;

            var pieceType = Types.TypeOf(piece);
            var toRank = (int)Types.RankOf(move.To);
            var toFile = (int)Types.FileOf(move.To);

            // Simple piece-square tables (positive for center control)
            int bonus = 0;

            // Centralization bonus
            int centerDistance = Math.Max(Math.Abs(toFile - 3), Math.Abs(toFile - 4)) +
                               Math.Max(Math.Abs(toRank - 3), Math.Abs(toRank - 4));
            bonus -= centerDistance * 5;

            // Piece-specific bonuses
            switch (pieceType)
            {
                case PieceType.Knight:
                    // Knights love the center
                    bonus -= centerDistance * 10;
                    break;

                case PieceType.Bishop:
                    // Bishops like long diagonals
                    if ((toFile == toRank) || (toFile + toRank == 7))
                        bonus += 10;
                    break;

                case PieceType.Rook:
                    // Rooks on 7th rank
                    if ((Types.ColorOf(piece) == Color.White && toRank == 6) ||
                        (Types.ColorOf(piece) == Color.Black && toRank == 1))
                        bonus += 20;
                    break;

                case PieceType.Pawn:
                    // Passed pawn bonus (simplified)
                    if (Types.ColorOf(piece) == Color.White)
                        bonus += toRank * 5;
                    else
                        bonus += (7 - toRank) * 5;
                    break;
            }

            return bonus;
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