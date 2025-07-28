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

        private static readonly StaticExchangeEvaluator seeEvaluator = new StaticExchangeEvaluator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Evaluate(Position position)
        {
            int score = 0;

            // Material and piece-square evaluation
            //score += EvaluatePieceType(position, PieceType.Pawn, PAWN_VALUE, PAWN_PST);
            //score += EvaluatePieceType(position, PieceType.Knight, KNIGHT_VALUE, KNIGHT_PST);
            //score += EvaluatePieceType(position, PieceType.Bishop, BISHOP_VALUE, BISHOP_PST);
            //score += EvaluatePieceType(position, PieceType.Rook, ROOK_VALUE, ROOK_PST);
            //score += EvaluatePieceType(position, PieceType.Queen, QUEEN_VALUE, QUEEN_PST);
            //score += EvaluatePieceType(position, PieceType.King, 0, KING_MIDDLEGAME_PST);

            // CRITICAL FIX: Tactical evaluation must consider side to move!
            score += EvaluateTactical(position);

            // Basic mobility bonus
            score += EvaluateMobility(position);

            // Bishop pair bonus
            if (Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Bishop)) >= 2)
                score += 50;
            if (Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Bishop)) >= 2)
                score -= 50;

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

    // StaticExchangeEvaluator class remains the same...
    public class StaticExchangeEvaluator
    {
        // Piece values for SEE (in centipawns)
        private static readonly int[] SEE_PIECE_VALUES = new int[]
        {
            100,   // Pawn
            320,   // Knight
            330,   // Bishop
            500,   // Rook
            900,   // Queen
            10000, // King (should never be captured)
            0      // No piece
        };

        private readonly ulong[] attackersBySide = new ulong[2];
        private readonly int[] gainStack = new int[32];

        /// <summary>
        /// Performs static exchange evaluation on a move.
        /// Returns true if the expected material gain is >= threshold.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool SEE(Position position, Move.Move move, int threshold)
        {
            // Special cases
            if (move.Flags == MoveFlags.EnPassant)
                return threshold <= 0; // En passant always wins a pawn

            if ((move.Flags & MoveFlags.Promotions) != 0)
            {
                // Promotions are usually good
                var promotionGain = GetPromotionValue(move.Flags) - 100; // Subtract pawn value
                return promotionGain >= threshold;
            }

            var from = move.From;
            var to = move.To;

            // Get initial material balance
            var capturedPiece = position.At(to);
            if (capturedPiece == Piece.NoPiece)
                return threshold <= 0; // Non-capture moves

            var movingPiece = position.At(from);
            if (movingPiece == Piece.NoPiece)
                return false; // Invalid move

            var movingPieceType = Types.TypeOf(movingPiece);
            var capturedPieceType = Types.TypeOf(capturedPiece);

            // Early pruning - if we can't reach threshold even with free capture
            var capturedValue = SEE_PIECE_VALUES[(int)capturedPieceType];
            if (capturedValue < threshold)
                return false;

            // If capturing with a less valuable piece, it's likely good
            var movingValue = SEE_PIECE_VALUES[(int)movingPieceType];
            if (capturedValue >= movingValue)
                return true;

            // Need to do full SEE calculation
            return SEEFull(position, from, to, threshold);
        }

        /// <summary>
        /// Full SEE calculation that simulates the entire capture sequence
        /// </summary>
        private bool SEEFull(Position position, Square from, Square to, int threshold)
        {
            var movingPiece = position.At(from);
            var capturedPiece = position.At(to);

            if (movingPiece == Piece.NoPiece)
                return false;

            var sideToMove = Types.ColorOf(movingPiece);
            var movingPieceType = Types.TypeOf(movingPiece);
            var capturedValue = capturedPiece != Piece.NoPiece ?
                SEE_PIECE_VALUES[(int)Types.TypeOf(capturedPiece)] : 0;

            // Start with all pieces
            var occupied = (position.AllPieces(Color.White) | position.AllPieces(Color.Black));

            // Make the initial capture
            occupied &= ~(1UL << (int)from);
            occupied |= (1UL << (int)to);

            // Find all attackers to the destination square
            attackersBySide[(int)Color.White] = GetAttackers(position, to, occupied, Color.White);
            attackersBySide[(int)Color.Black] = GetAttackers(position, to, occupied, Color.Black);

            // Remove the moving piece from attackers (it already moved)
            attackersBySide[(int)sideToMove] &= ~(1UL << (int)from);

            // Initialize gain stack
            var depth = 0;
            gainStack[depth] = capturedValue;
            var attackingPieceValue = SEE_PIECE_VALUES[(int)movingPieceType];

            var side = sideToMove.Flip();

            // Simulate the capture sequence
            while (true)
            {
                depth++;
                gainStack[depth] = attackingPieceValue - gainStack[depth - 1];

                // Find least valuable attacker
                var attackers = attackersBySide[(int)side];
                if (attackers == 0)
                    break;

                // Find least valuable attacker
                var attacker = GetLeastValuableAttacker(position, attackers, side, out attackingPieceValue);
                if (attacker == Square.NoSquare)
                    break;

                // King captures end the sequence
                if (attackingPieceValue >= SEE_PIECE_VALUES[(int)PieceType.King])
                    break;

                // Remove this attacker and update occupancy
                occupied &= ~(1UL << (int)attacker);
                attackersBySide[(int)side] &= ~(1UL << (int)attacker);

                // Update attacks due to discovered attackers (X-ray)
                UpdateXrayAttacks(position, attacker, to, occupied, attackersBySide);

                side = side.Flip();
            }

            // Minimax the gain stack
            while (--depth > 0)
            {
                gainStack[depth - 1] = -Math.Max(-gainStack[depth - 1], gainStack[depth]);
            }

            return gainStack[0] >= threshold;
        }

        /// <summary>
        /// Get all pieces of 'side' that attack 'square' with occupancy 'occupied'
        /// </summary>
        private ulong GetAttackers(Position position, Square square, ulong occupied, Color side)
        {
            ulong attackers = 0;

            // Pawn attacks
            attackers |= Tables.PawnAttacks(side.Flip(), square) & position.BitboardOf(side, PieceType.Pawn);

            // Knight attacks
            attackers |= Tables.Attacks(PieceType.Knight, square, occupied) & position.BitboardOf(side, PieceType.Knight);

            // Bishop/Queen diagonal attacks
            ulong diagonalAttacks = Tables.Attacks(PieceType.Bishop, square, occupied);
            attackers |= diagonalAttacks & position.DiagonalSliders(side);

            // Rook/Queen orthogonal attacks
            ulong orthogonalAttacks = Tables.Attacks(PieceType.Rook, square, occupied);
            attackers |= orthogonalAttacks & position.OrthogonalSliders(side);

            // King attacks (always included even though king captures usually end the sequence)
            attackers |= Tables.Attacks(PieceType.King, square, occupied) & position.BitboardOf(side, PieceType.King);

            return attackers;
        }

        /// <summary>
        /// Find the least valuable piece in the attacker set
        /// </summary>
        private Square GetLeastValuableAttacker(Position position, ulong attackers, Color side, out int pieceValue)
        {
            // Check pieces in order of value: pawn, knight, bishop, rook, queen, king
            for (PieceType pt = PieceType.Pawn; pt <= PieceType.King; pt++)
            {
                var pieceBB = position.BitboardOf(side, pt) & attackers;
                if (pieceBB != 0)
                {
                    pieceValue = SEE_PIECE_VALUES[(int)pt];
                    return Bitboard.Bsf(pieceBB); // Get first piece of this type
                }
            }

            pieceValue = 0;
            return Square.NoSquare;
        }

        /// <summary>
        /// Update attacker bitboards for X-ray attacks after a piece moves
        /// </summary>
        private void UpdateXrayAttacks(Position position, Square movedFrom, Square target, ulong occupied, ulong[] attackersBySide)
        {
            // Check if the moved piece was blocking any sliders
            var direction = GetDirection(movedFrom, target);
            if (direction == Direction.North) return; // No direction = no x-rays

            // Find potential x-ray attackers behind the moved piece
            var xraySquare = movedFrom;

            // Walk in the opposite direction to find x-ray attackers
            while (true)
            {
                xraySquare = GetNextSquare(xraySquare, GetOppositeDirection(direction));
                if (xraySquare == Square.NoSquare)
                    break;

                if ((occupied & (1UL << (int)xraySquare)) != 0)
                {
                    // Found a piece - check if it's a slider that attacks along this ray
                    var piece = position.At(xraySquare);
                    if (piece != Piece.NoPiece)
                    {
                        var pieceType = Types.TypeOf(piece);
                        var color = Types.ColorOf(piece);

                        bool isXrayAttacker = false;
                        if (IsDiagonalDirection(direction) &&
                            (pieceType == PieceType.Bishop || pieceType == PieceType.Queen))
                        {
                            isXrayAttacker = true;
                        }
                        else if (IsOrthogonalDirection(direction) &&
                                (pieceType == PieceType.Rook || pieceType == PieceType.Queen))
                        {
                            isXrayAttacker = true;
                        }

                        if (isXrayAttacker)
                        {
                            // Verify this piece actually attacks the target
                            var attacks = pieceType == PieceType.Bishop ?
                                Tables.Attacks(PieceType.Bishop, xraySquare, occupied) :
                                Tables.Attacks(PieceType.Rook, xraySquare, occupied);

                            if ((attacks & (1UL << (int)target)) != 0)
                            {
                                attackersBySide[(int)color] |= (1UL << (int)xraySquare);
                            }
                        }
                    }
                    break; // Stop at first piece
                }
            }
        }

        // Helper methods for directions
        private Direction GetDirection(Square from, Square to)
        {
            int fromFile = (int)Types.FileOf(from);
            int fromRank = (int)Types.RankOf(from);
            int toFile = (int)Types.FileOf(to);
            int toRank = (int)Types.RankOf(to);

            int fileDiff = toFile - fromFile;
            int rankDiff = toRank - fromRank;

            if (fileDiff == 0 && rankDiff > 0) return Direction.North;
            if (fileDiff == 0 && rankDiff < 0) return Direction.South;
            if (fileDiff > 0 && rankDiff == 0) return Direction.East;
            if (fileDiff < 0 && rankDiff == 0) return Direction.West;
            if (fileDiff > 0 && rankDiff > 0 && fileDiff == rankDiff) return Direction.NorthEast;
            if (fileDiff < 0 && rankDiff > 0 && -fileDiff == rankDiff) return Direction.NorthWest;
            if (fileDiff > 0 && rankDiff < 0 && fileDiff == -rankDiff) return Direction.SouthEast;
            if (fileDiff < 0 && rankDiff < 0 && fileDiff == rankDiff) return Direction.SouthWest;

            return Direction.North; // No clear direction
        }

        private Direction GetOppositeDirection(Direction dir)
        {
            return dir switch
            {
                Direction.North => Direction.South,
                Direction.South => Direction.North,
                Direction.East => Direction.West,
                Direction.West => Direction.East,
                Direction.NorthEast => Direction.SouthWest,
                Direction.NorthWest => Direction.SouthEast,
                Direction.SouthEast => Direction.NorthWest,
                Direction.SouthWest => Direction.NorthEast,
                _ => Direction.North
            };
        }

        private Square GetNextSquare(Square sq, Direction dir)
        {
            int file = (int)Types.FileOf(sq);
            int rank = (int)Types.RankOf(sq);

            switch (dir)
            {
                case Direction.North: rank++; break;
                case Direction.South: rank--; break;
                case Direction.East: file++; break;
                case Direction.West: file--; break;
                case Direction.NorthEast: file++; rank++; break;
                case Direction.NorthWest: file--; rank++; break;
                case Direction.SouthEast: file++; rank--; break;
                case Direction.SouthWest: file--; rank--; break;
            }

            if (file < 0 || file > 7 || rank < 0 || rank > 7)
                return Square.NoSquare;

            return Types.CreateSquare((Move.File)file, (Rank)rank);
        }

        private bool IsDiagonalDirection(Direction dir)
        {
            return dir == Direction.NorthEast || dir == Direction.NorthWest ||
                   dir == Direction.SouthEast || dir == Direction.SouthWest;
        }

        private bool IsOrthogonalDirection(Direction dir)
        {
            return dir == Direction.North || dir == Direction.South ||
                   dir == Direction.East || dir == Direction.West;
        }

        private int GetPromotionValue(MoveFlags flags)
        {
            return flags switch
            {
                MoveFlags.PrQueen or MoveFlags.PcQueen => 900,
                MoveFlags.PrRook or MoveFlags.PcRook => 500,
                MoveFlags.PrBishop or MoveFlags.PcBishop => 330,
                MoveFlags.PrKnight or MoveFlags.PcKnight => 320,
                _ => 0
            };
        }
    }
}