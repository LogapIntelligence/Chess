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
                            int[,] historyTable, Position position, StaticExchangeEvaluator see, Move.Move counterMove = default)
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
                    moveScores[i] = ScoreCapture(move, position, see);
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
                            int[,] historyTable, Position position, Move.Move counterMove, StaticExchangeEvaluator seeEvaluator)
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
                    moveScores[i] = ScoreCapture(move, position, seeEvaluator);
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

                    // Penalty for moves that might hang pieces (quick check)
                    if (IsPotentiallyHangingMove(move, position))
                    {
                        moveScores[i] -= 50000;
                    }
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
        public int OrderCaptures(Move.Move[] moves, int moveCount, Position position, StaticExchangeEvaluator see)
        {
            // Score captures using MVV-LVA with SEE
            for (int i = 0; i < moveCount; i++)
            {
                moveScores[i] = ScoreCapture(moves[i], position, see);
            }

            // Full sort for captures since they're usually fewer
            Array.Sort(moveScores, moves, 0, moveCount);
            Array.Reverse(moves, 0, moveCount);

            return moveCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ScoreCapture(Move.Move move, Position position, StaticExchangeEvaluator seeEvaluator)
        {
            // Special handling for promotions
            if ((move.Flags & MoveFlags.Promotions) != 0)
            {
                var promoValue = move.Flags switch
                {
                    MoveFlags.PrQueen or MoveFlags.PcQueen => 900,
                    MoveFlags.PrRook or MoveFlags.PcRook => 500,
                    MoveFlags.PrBishop or MoveFlags.PcBishop => 330,
                    MoveFlags.PrKnight or MoveFlags.PcKnight => 320,
                    _ => 0
                };

                // Promotion captures are almost always good
                if (move.IsCapture)
                    return GOOD_CAPTURE_SCORE + promoValue;
                else
                    return GOOD_CAPTURE_SCORE + promoValue - 100;
            }

            // En passant is always a good capture
            if (move.Flags == MoveFlags.EnPassant)
                return GOOD_CAPTURE_SCORE + 100;

            // Use SEE for regular captures
            var seeValue = 0;
            if (seeEvaluator.SEE(position, move, 0))
            {
                // Winning or equal capture
                var captured = position.At(move.To);
                var capturedValue = GetPieceValue(Types.TypeOf(captured));

                if (seeEvaluator.SEE(position, move, 1))
                {
                    // Clearly winning capture
                    return GOOD_CAPTURE_SCORE + capturedValue;
                }
                else
                {
                    // Equal capture
                    return EQUAL_CAPTURE_SCORE + capturedValue;
                }
            }
            else
            {
                // Losing capture - order by least loss
                var captured = position.At(move.To);
                var attacker = position.At(move.From);
                var materialDiff = GetPieceValue(Types.TypeOf(captured)) - GetPieceValue(Types.TypeOf(attacker));

                return BAD_CAPTURE_SCORE + materialDiff;
            }
        }

        private bool IsPotentiallyHangingMove(Move.Move move, Position position)
        {
            // Quick heuristic - moving to a square attacked by enemy pawns
            var movingPiece = position.At(move.From);
            if (movingPiece == Piece.NoPiece)
                return false;

            var pieceType = Types.TypeOf(movingPiece);
            var color = Types.ColorOf(movingPiece);

            // Don't check pawns (too cheap) or king (can't hang)
            if (pieceType == PieceType.Pawn || pieceType == PieceType.King)
                return false;

            // Check if destination is attacked by enemy pawns
            var enemyPawnAttacks = Tables.PawnAttacks(color, move.To);
            var enemyPawns = position.BitboardOf(color.Flip(), PieceType.Pawn);

            if ((enemyPawnAttacks & enemyPawns) != 0)
            {
                // Moving to a square attacked by enemy pawns - likely bad unless defended
                var ourPawnDefense = Tables.PawnAttacks(color.Flip(), move.To) & position.BitboardOf(color, PieceType.Pawn);
                return ourPawnDefense == 0;
            }

            return false;
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