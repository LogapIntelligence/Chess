using Move;
using System;
using System.Runtime.CompilerServices;

namespace Search
{
    public static class Evaluation
    {
        // Piece values
        private const int PAWN_VALUE = 100;
        private const int KNIGHT_VALUE = 320;
        private const int BISHOP_VALUE = 330;
        private const int ROOK_VALUE = 500;
        private const int QUEEN_VALUE = 900;
        private const int KING_VALUE = 20000;

        // Piece-square tables (from White's perspective)
        private static readonly int[] PAWN_PST = new int[]
        {
             0,  0,  0,  0,  0,  0,  0,  0,
            50, 50, 50, 50, 50, 50, 50, 50,
            10, 10, 20, 30, 30, 20, 10, 10,
             5,  5, 10, 25, 25, 10,  5,  5,
             0,  0,  0, 20, 20,  0,  0,  0,
             5, -5,-10,  0,  0,-10, -5,  5,
             5, 10, 10,-20,-20, 10, 10,  5,
             0,  0,  0,  0,  0,  0,  0,  0
        };

        private static readonly int[] KNIGHT_PST = new int[]
        {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50
        };

        private static readonly int[] BISHOP_PST = new int[]
        {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -20,-10,-10,-10,-10,-10,-10,-20
        };

        private static readonly int[] ROOK_PST = new int[]
        {
             0,  0,  0,  0,  0,  0,  0,  0,
             5, 10, 10, 10, 10, 10, 10,  5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
             0,  0,  0,  5,  5,  0,  0,  0
        };

        private static readonly int[] QUEEN_PST = new int[]
        {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5,  5,  5,  5,  0,-10,
             -5,  0,  5,  5,  5,  5,  0, -5,
              0,  0,  5,  5,  5,  5,  0, -5,
            -10,  5,  5,  5,  5,  5,  0,-10,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20
        };

        private static readonly int[] KING_MIDDLEGAME_PST = new int[]
        {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
             20, 20,  0,  0,  0,  0, 20, 20,
             20, 30, 10,  0,  0, 10, 30, 20
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Evaluate(Position position)
        {
            int score = 0;

            // Material and piece-square evaluation
            score += EvaluatePieceType(position, PieceType.Pawn, PAWN_VALUE, PAWN_PST);
            score += EvaluatePieceType(position, PieceType.Knight, KNIGHT_VALUE, KNIGHT_PST);
            score += EvaluatePieceType(position, PieceType.Bishop, BISHOP_VALUE, BISHOP_PST);
            score += EvaluatePieceType(position, PieceType.Rook, ROOK_VALUE, ROOK_PST);
            score += EvaluatePieceType(position, PieceType.Queen, QUEEN_VALUE, QUEEN_PST);
            score += EvaluatePieceType(position, PieceType.King, 0, KING_MIDDLEGAME_PST);

            // CRITICAL FIX: Tactical evaluation must consider side to move!
            //score += EvaluateTactical(position);

            // Basic mobility bonus
            //score += EvaluateMobility(position);

            // Bishop pair bonus
            //if (Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Bishop)) >= 2)
               // score += 50;
           // if (Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Bishop)) >= 2)
               // score -= 50;

            // Return score from the perspective of the side to move
            return position.Turn == Color.White ? score : -score;
        }

        /// <summary>
        /// Evaluate tactical aspects - hanging pieces, undefended pieces, threats
        /// FIXED: Now properly accounts for side to move
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EvaluateTactical(Position position)
        {
            int score = 0;
            var occupied = position.AllPieces(Color.White) | position.AllPieces(Color.Black);

            // CRITICAL: Evaluate hanging pieces with consideration for who moves next
            var whiteHanging = EvaluateHangingPieces(position, Color.White, occupied, position.Turn == Color.White);
            var blackHanging = EvaluateHangingPieces(position, Color.Black, occupied, position.Turn == Color.Black);

            score += whiteHanging;
            score -= blackHanging;

            return score;
        }

        /// <summary>
        /// Detect and penalize hanging pieces (undefended pieces under attack)
        /// FIXED: Now considers whether it's this side's turn to move
        /// </summary>
        private static int EvaluateHangingPieces(Position position, Color color, ulong occupied, bool isOurTurn)
        {
            int penalty = 0;
            var enemyColor = color.Flip();

            // Check each piece type (skip king)
            for (var pt = PieceType.Pawn; pt <= PieceType.Queen; pt++)
            {
                var pieces = position.BitboardOf(color, pt);
                while (pieces != 0)
                {
                    var sq = Bitboard.PopLsb(ref pieces);

                    // Check if piece is attacked
                    var attackers = GetAttackers(position, sq, occupied, enemyColor);
                    if (attackers != 0)
                    {
                        // Check if piece is defended
                        var defenders = GetAttackers(position, sq, occupied, color);

                        // CRITICAL FIX: If it's our turn and we have a hanging piece,
                        // the penalty should be much less severe because we can save it!
                        if (isOurTurn)
                        {
                            // It's our turn - we can potentially save the piece
                            if (defenders == 0)
                            {
                                // Undefended but we can move it
                                penalty -= GetPieceValue(pt) / 4; // Much smaller penalty
                            }
                            else
                            {
                                // Defended, so even less penalty
                                var leastAttackerValue = GetLeastAttackerValue(position, attackers, enemyColor);
                                var pieceValue = GetPieceValue(pt);

                                if (leastAttackerValue < pieceValue)
                                {
                                    // We might lose material but can avoid it
                                    penalty -= (pieceValue - leastAttackerValue) / 8;
                                }
                            }
                        }
                        else
                        {
                            // Opponent's turn - full penalty for hanging pieces
                            if (defenders == 0)
                            {
                                // Piece is completely hanging and will be captured!
                                penalty -= GetPieceValue(pt);
                            }
                            else
                            {
                                // Use simplified SEE check - if we would lose material defending
                                var leastAttackerValue = GetLeastAttackerValue(position, attackers, enemyColor);
                                var pieceValue = GetPieceValue(pt);

                                if (leastAttackerValue < pieceValue)
                                {
                                    // We lose material in the exchange
                                    penalty -= (pieceValue - leastAttackerValue) / 2;
                                }
                            }
                        }
                    }
                }
            }

            return penalty;
        }

        /// <summary>
        /// Get all attackers of a square
        /// </summary>
        private static ulong GetAttackers(Position position, Square square, ulong occupied, Color attackerColor)
        {
            ulong attackers = 0;

            // Pawn attacks
            attackers |= Tables.PawnAttacks(attackerColor.Flip(), square) & position.BitboardOf(attackerColor, PieceType.Pawn);

            // Knight attacks
            attackers |= Tables.Attacks(PieceType.Knight, square, occupied) & position.BitboardOf(attackerColor, PieceType.Knight);

            // Bishop/Queen diagonal attacks
            ulong diagonalAttacks = Tables.Attacks(PieceType.Bishop, square, occupied);
            attackers |= diagonalAttacks & position.DiagonalSliders(attackerColor);

            // Rook/Queen orthogonal attacks
            ulong orthogonalAttacks = Tables.Attacks(PieceType.Rook, square, occupied);
            attackers |= orthogonalAttacks & position.OrthogonalSliders(attackerColor);

            // King attacks
            attackers |= Tables.Attacks(PieceType.King, square, occupied) & position.BitboardOf(attackerColor, PieceType.King);

            return attackers;
        }

        /// <summary>
        /// Find the value of the least valuable attacker
        /// </summary>
        private static int GetLeastAttackerValue(Position position, ulong attackers, Color color)
        {
            // Check pieces in order of value
            if ((attackers & position.BitboardOf(color, PieceType.Pawn)) != 0)
                return PAWN_VALUE;
            if ((attackers & position.BitboardOf(color, PieceType.Knight)) != 0)
                return KNIGHT_VALUE;
            if ((attackers & position.BitboardOf(color, PieceType.Bishop)) != 0)
                return BISHOP_VALUE;
            if ((attackers & position.BitboardOf(color, PieceType.Rook)) != 0)
                return ROOK_VALUE;
            if ((attackers & position.BitboardOf(color, PieceType.Queen)) != 0)
                return QUEEN_VALUE;
            if ((attackers & position.BitboardOf(color, PieceType.King)) != 0)
                return KING_VALUE;

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EvaluatePieceType(Position position, PieceType pieceType, int pieceValue, int[] pst)
        {
            int score = 0;

            // White pieces
            ulong whitePieces = position.BitboardOf(Color.White, pieceType);
            while (whitePieces != 0)
            {
                Square sq = Bitboard.PopLsb(ref whitePieces);
                score += pieceValue;
                score += pst[(int)sq];
            }

            // Black pieces (flip square for PST lookup)
            ulong blackPieces = position.BitboardOf(Color.Black, pieceType);
            while (blackPieces != 0)
            {
                Square sq = Bitboard.PopLsb(ref blackPieces);
                score -= pieceValue;
                // Flip square vertically for black's perspective
                int flippedSq = (int)sq ^ 56;
                score -= pst[flippedSq];
            }

            return score;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EvaluateMobility(Position position)
        {
            int score = 0;
            ulong occupied = position.AllPieces(Color.White) | position.AllPieces(Color.Black);

            // Knight mobility
            score += EvaluatePieceMobility(position, Color.White, PieceType.Knight, occupied, 4);
            score -= EvaluatePieceMobility(position, Color.Black, PieceType.Knight, occupied, 4);

            // Bishop mobility
            score += EvaluatePieceMobility(position, Color.White, PieceType.Bishop, occupied, 3);
            score -= EvaluatePieceMobility(position, Color.Black, PieceType.Bishop, occupied, 3);

            // Rook mobility
            score += EvaluatePieceMobility(position, Color.White, PieceType.Rook, occupied, 2);
            score -= EvaluatePieceMobility(position, Color.Black, PieceType.Rook, occupied, 2);

            // Queen mobility
            score += EvaluatePieceMobility(position, Color.White, PieceType.Queen, occupied, 1);
            score -= EvaluatePieceMobility(position, Color.Black, PieceType.Queen, occupied, 1);

            return score;
        }

        private static int EvaluatePieceMobility(Position position, Color color, PieceType pieceType, ulong occupied, int weight)
        {
            int mobility = 0;
            ulong pieces = position.BitboardOf(color, pieceType);

            while (pieces != 0)
            {
                Square sq = Bitboard.PopLsb(ref pieces);
                ulong moves = Tables.Attacks(pieceType, sq, occupied) & ~position.AllPieces(color);
                mobility += Bitboard.PopCount(moves) * weight;
            }

            return mobility;
        }

        private static int GetPieceValue(PieceType pt)
        {
            return pt switch
            {
                PieceType.Pawn => PAWN_VALUE,
                PieceType.Knight => KNIGHT_VALUE,
                PieceType.Bishop => BISHOP_VALUE,
                PieceType.Rook => ROOK_VALUE,
                PieceType.Queen => QUEEN_VALUE,
                PieceType.King => KING_VALUE,
                _ => 0
            };
        }
    }
}