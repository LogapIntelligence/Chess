using System;
using Move;

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
        private const int KING_VALUE = 10000;

        // Game phase thresholds
        private const int TOTAL_PHASE = 24; // 4 knights + 4 bishops + 4 rooks + 2 queens

        // Evaluation weights
        private const int PIECE_DEVELOPMENT_WEIGHT = 10;
        private const int CENTER_CONTROL_WEIGHT = 15;
        private const int SPACE_WEIGHT = 5;
        private const int KING_SAFETY_WEIGHT = 30;
        private const int PAWN_STRUCTURE_WEIGHT = 20;
        private const int ROOK_CONNECTION_BONUS = 15;
        private const int CASTLING_BONUS = 25;
        private const int EARLY_QUEEN_PENALTY = 30;

        // Piece-square tables for positional evaluation
        private static readonly int[] PawnTable = {
             0,  0,  0,  0,  0,  0,  0,  0,
            50, 50, 50, 50, 50, 50, 50, 50,
            10, 10, 20, 30, 30, 20, 10, 10,
             5,  5, 10, 25, 25, 10,  5,  5,
             0,  0,  0, 40, 40,  0,  0,  0,
             5, -5,-10,  5,  5,-10, -5,  5,
             5, 10, 10,-20,-20, 10, 10,  5,
             0,  0,  0,  0,  0,  0,  0,  0
        };

        private static readonly int[] KnightTable = {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 0, 15, 15, 0,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50
        };

        private static readonly int[] BishopTable = {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -20,-10,-10,-10,-10,-10,-10,-20
        };

        private static readonly int[] RookTable = {
             0,  0,  0,  0,  0,  0,  0,  0,
             5, 10, 10, 10, 10, 10, 10,  5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
             0,  0,  0,  5,  5,  0,  0,  0
        };

        private static readonly int[] QueenTable = {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5,  5,  5,  5,  0,-10,
             -5,  0,  5,  5,  5,  5,  0, -5,
              0,  0,  5,  5,  5,  5,  0, -5,
            -10,  5,  5,  5,  5,  5,  0,-10,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20
        };

        private static readonly int[] KingMiddleGameTable = {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
             20, 20,  0,  0,  0,  0, 20, 20,
             20, 30, 10,  0,  0, 10, 30, 20
        };

        private static readonly int[] KingEndGameTable = {
            -50,-40,-30,-20,-20,-30,-40,-50,
            -30,-20,-10,  0,  0,-10,-20,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-30,  0,  0,  0,  0,-30,-30,
            -50,-30,-30,-30,-30,-30,-30,-50
        };

        // Center squares for control evaluation
        private static readonly ulong CENTER_SQUARES = 0x0000001818000000UL; // d4, e4, d5, e5
        private static readonly ulong EXTENDED_CENTER = 0x00003C3C3C3C0000UL; // c3-f3 to c6-f6

        public static int Evaluate(Position position)
        {
            int score = 0;

            // Calculate game phase (0 = opening, 256 = endgame)
            int phase = CalculateGamePhase(position);

            // Material and positional evaluation for both sides
            var whiteEval = EvaluateSide(position, Color.White, phase);
            var blackEval = EvaluateSide(position, Color.Black, phase);

            score += whiteEval;
            score -= blackEval;

            // Additional positional factors
            score += EvaluatePieceDevelopment(position);
            score += EvaluateCenterControl(position);
            score += EvaluateSpace(position);
            score += EvaluateAdvancedPawnStructure(position);
            score += EvaluateKingSafety(position, phase);
            score += EvaluateRookConnection(position);
            score += EvaluateQueenDevelopment(position);
            score += EvaluatePiecePlacement(position);
            score += EvaluatePassedPawns(position);
            score += EvaluateBishopMobility(position);

            return position.Turn == Color.White ? score : -score;
        }

        private static int CalculateGamePhase(Position position)
        {
            int phase = TOTAL_PHASE;

            phase -= Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Knight)) * 1;
            phase -= Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Knight)) * 1;
            phase -= Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Bishop)) * 1;
            phase -= Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Bishop)) * 1;
            phase -= Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Rook)) * 2;
            phase -= Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Rook)) * 2;
            phase -= Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Queen)) * 4;
            phase -= Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Queen)) * 4;

            return (phase * 256 + TOTAL_PHASE / 2) / TOTAL_PHASE;
        }

        private static int EvaluateSide(Position position, Color color, int phase)
        {
            int score = 0;

            // Pawns
            var pawns = position.BitboardOf(color, PieceType.Pawn);
            score += Bitboard.PopCount(pawns) * PAWN_VALUE;
            score += EvaluatePieceSquares(pawns, PawnTable, color);

            // Knights
            var knights = position.BitboardOf(color, PieceType.Knight);
            score += Bitboard.PopCount(knights) * KNIGHT_VALUE;
            score += EvaluatePieceSquares(knights, KnightTable, color);

            // Bishops
            var bishops = position.BitboardOf(color, PieceType.Bishop);
            score += Bitboard.PopCount(bishops) * BISHOP_VALUE;
            score += EvaluatePieceSquares(bishops, BishopTable, color);

            // Bishop pair bonus
            if (Bitboard.PopCount(bishops) >= 2)
                score += 30;

            // Rooks
            var rooks = position.BitboardOf(color, PieceType.Rook);
            score += Bitboard.PopCount(rooks) * ROOK_VALUE;
            score += EvaluatePieceSquares(rooks, RookTable, color);

            // Queens
            var queens = position.BitboardOf(color, PieceType.Queen);
            score += Bitboard.PopCount(queens) * QUEEN_VALUE;
            score += EvaluatePieceSquares(queens, QueenTable, color);

            // King - interpolate between middlegame and endgame tables
            var king = position.BitboardOf(color, PieceType.King);
            int mgScore = EvaluatePieceSquares(king, KingMiddleGameTable, color);
            int egScore = EvaluatePieceSquares(king, KingEndGameTable, color);
            score += ((mgScore * phase + egScore * (256 - phase)) / 256);

            return score;
        }

        private static int EvaluatePieceSquares(ulong bitboard, int[] table, Color color)
        {
            int score = 0;
            while (bitboard != 0)
            {
                var square = Bitboard.PopLsb(ref bitboard);
                var index = color == Color.White ? (int)square : (int)square ^ 56; // Flip for black
                score += table[index];
            }
            return score;
        }

        // 1. Evaluate piece development
        private static int EvaluatePieceDevelopment(Position position)
        {
            int score = 0;

            // White development
            var whiteKnights = position.BitboardOf(Color.White, PieceType.Knight);
            var whiteBishops = position.BitboardOf(Color.White, PieceType.Bishop);

            // Knights not on starting squares (b1, g1)
            score += Bitboard.PopCount(whiteKnights & ~0x42UL) * PIECE_DEVELOPMENT_WEIGHT;
            // Bishops not on starting squares (c1, f1)
            score += Bitboard.PopCount(whiteBishops & ~0x24UL) * PIECE_DEVELOPMENT_WEIGHT;
            // Bonus for knights on good central squares
            score += Bitboard.PopCount(whiteKnights & 0x0000183C18000000UL) * 5;

            // Black development
            var blackKnights = position.BitboardOf(Color.Black, PieceType.Knight);
            var blackBishops = position.BitboardOf(Color.Black, PieceType.Bishop);

            // Knights not on starting squares (b8, g8)
            score -= Bitboard.PopCount(blackKnights & ~0x4200000000000000UL) * PIECE_DEVELOPMENT_WEIGHT;
            // Bishops not on starting squares (c8, f8)
            score -= Bitboard.PopCount(blackBishops & ~0x2400000000000000UL) * PIECE_DEVELOPMENT_WEIGHT;
            // Bonus for knights on good central squares
            score -= Bitboard.PopCount(blackKnights & 0x0000183C18000000UL) * 5;

            return score;
        }

        // 2. Evaluate center control
        private static int EvaluateCenterControl(Position position)
        {
            int score = 0;

            // Pawn control of center
            var whitePawns = position.BitboardOf(Color.White, PieceType.Pawn);
            var blackPawns = position.BitboardOf(Color.Black, PieceType.Pawn);

            // Direct center control by pawns
            score += Bitboard.PopCount(whitePawns & CENTER_SQUARES) * CENTER_CONTROL_WEIGHT * 2;
            score -= Bitboard.PopCount(blackPawns & CENTER_SQUARES) * CENTER_CONTROL_WEIGHT * 2;

            // Extended center control
            score += Bitboard.PopCount(whitePawns & EXTENDED_CENTER) * CENTER_CONTROL_WEIGHT;
            score -= Bitboard.PopCount(blackPawns & EXTENDED_CENTER) * CENTER_CONTROL_WEIGHT;

            // Control by pawn attacks
            var whitePawnAttacks = Tables.PawnAttacks(Color.White, whitePawns);
            var blackPawnAttacks = Tables.PawnAttacks(Color.Black, blackPawns);

            score += Bitboard.PopCount(whitePawnAttacks & CENTER_SQUARES) * CENTER_CONTROL_WEIGHT / 2;
            score -= Bitboard.PopCount(blackPawnAttacks & CENTER_SQUARES) * CENTER_CONTROL_WEIGHT / 2;

            return score;
        }

        // 3. Evaluate space coverage
        private static int EvaluateSpace(Position position)
        {
            int score = 0;

            var allPieces = position.AllPieces(Color.White) | position.AllPieces(Color.Black);

            // Count squares controlled in opponent's half
            var whiteControl = 0UL;
            var blackControl = 0UL;

            // Add pawn attacks
            whiteControl |= Tables.PawnAttacks(Color.White, position.BitboardOf(Color.White, PieceType.Pawn));
            blackControl |= Tables.PawnAttacks(Color.Black, position.BitboardOf(Color.Black, PieceType.Pawn));

            // Add knight attacks
            var whiteKnights = position.BitboardOf(Color.White, PieceType.Knight);
            while (whiteKnights != 0)
            {
                var sq = Bitboard.PopLsb(ref whiteKnights);
                whiteControl |= Tables.Attacks(PieceType.Knight, sq, allPieces);
            }

            var blackKnights = position.BitboardOf(Color.Black, PieceType.Knight);
            while (blackKnights != 0)
            {
                var sq = Bitboard.PopLsb(ref blackKnights);
                blackControl |= Tables.Attacks(PieceType.Knight, sq, allPieces);
            }

            // Space in enemy territory (ranks 5-8 for white, ranks 1-4 for black)
            const ulong BLACK_HALF = 0xFFFFFFFF00000000UL;
            const ulong WHITE_HALF = 0x00000000FFFFFFFFUL;

            score += Bitboard.PopCount(whiteControl & BLACK_HALF) * SPACE_WEIGHT;
            score -= Bitboard.PopCount(blackControl & WHITE_HALF) * SPACE_WEIGHT;

            // Mobility bonus based on available moves
            score += Bitboard.PopCount(whiteControl & ~position.AllPieces(Color.White)) * 2;
            score -= Bitboard.PopCount(blackControl & ~position.AllPieces(Color.Black)) * 2;

            return score;
        }

        // 4. Advanced pawn structure evaluation
        private static int EvaluateAdvancedPawnStructure(Position position)
        {
            int score = 0;

            var whitePawns = position.BitboardOf(Color.White, PieceType.Pawn);
            var blackPawns = position.BitboardOf(Color.Black, PieceType.Pawn);

            for (int file = 0; file < 8; file++)
            {
                var fileMask = Bitboard.MASK_FILE[file];
                var whitePawnsOnFile = Bitboard.PopCount(whitePawns & fileMask);
                var blackPawnsOnFile = Bitboard.PopCount(blackPawns & fileMask);

                if (whitePawnsOnFile > 1)
                    score -= (whitePawnsOnFile - 1) * 15;

                if (blackPawnsOnFile > 1)
                    score += (blackPawnsOnFile - 1) * 15;
            }

            // Isolated pawns penalty
            for (int file = 0; file < 8; file++)
            {
                var fileMask = Bitboard.MASK_FILE[file];
                var leftFile = file > 0 ? Bitboard.MASK_FILE[file - 1] : 0UL;
                var rightFile = file < 7 ? Bitboard.MASK_FILE[file + 1] : 0UL;
                var adjacentFiles = leftFile | rightFile;

                if ((whitePawns & fileMask) != 0 && (whitePawns & adjacentFiles) == 0)
                    score -= 20;

                if ((blackPawns & fileMask) != 0 && (blackPawns & adjacentFiles) == 0)
                    score += 20;
            }
            // Now add: backward pawns, pawn chains, pawn storms

            // Backward pawns
            for (int file = 0; file < 8; file++)
            {
                var fileMask = Bitboard.MASK_FILE[file];
                var adjacentFiles = 0UL;
                if (file > 0) adjacentFiles |= Bitboard.MASK_FILE[file - 1];
                if (file < 7) adjacentFiles |= Bitboard.MASK_FILE[file + 1];

                // White backward pawns
                var whitePawnsOnFile = whitePawns & fileMask;
                if (whitePawnsOnFile != 0)
                {
                    var pawn = Bitboard.Bsf(whitePawnsOnFile); // Get lowest pawn
                    var rank = Types.RankOf(pawn);
                    var supportingPawns = whitePawns & adjacentFiles & Bitboard.MASK_RANK[(int)rank - 1];
                    if (supportingPawns == 0 && rank > Rank.Rank2)
                    {
                        score -= 15; // Backward pawn penalty
                    }
                }

                // Black backward pawns
                var blackPawnsOnFile = blackPawns & fileMask;
                if (blackPawnsOnFile != 0)
                {
                    var pawn = Bitboard.Bsf(blackPawnsOnFile); // Get highest pawn for black
                    var rank = Types.RankOf(pawn);
                    var supportingPawns = blackPawns & adjacentFiles & Bitboard.MASK_RANK[(int)rank + 1];
                    if (supportingPawns == 0 && rank < Rank.Rank7)
                    {
                        score += 15; // Backward pawn penalty for black
                    }
                }
            }

            // Pawn chains bonus
            var whitePawnChains = whitePawns & Bitboard.Shift(Direction.SouthEast, whitePawns);
            whitePawnChains |= whitePawns & Bitboard.Shift(Direction.SouthWest, whitePawns);
            score += Bitboard.PopCount(whitePawnChains) * 5;

            var blackPawnChains = blackPawns & Bitboard.Shift(Direction.NorthEast, blackPawns);
            blackPawnChains |= blackPawns & Bitboard.Shift(Direction.NorthWest, blackPawns);
            score -= Bitboard.PopCount(blackPawnChains) * 5;

            return score * PAWN_STRUCTURE_WEIGHT / 10;
        }

        // 5. Evaluate queen development timing
        private static int EvaluateQueenDevelopment(Position position)
        {
            int score = 0;

            // Count developed minor pieces
            var whiteMinorsDeveloped =
                Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Knight) & ~0x42UL) +
                Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Bishop) & ~0x24UL);

            var blackMinorsDeveloped =
                Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Knight) & ~0x4200000000000000UL) +
                Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Bishop) & ~0x2400000000000000UL);

            // Check if queens are developed
            var whiteQueen = position.BitboardOf(Color.White, PieceType.Queen);
            var blackQueen = position.BitboardOf(Color.Black, PieceType.Queen);

            // Penalty for early queen development
            if (whiteQueen != 0 && (whiteQueen & 0x8UL) == 0) // Queen moved from d1
            {
                if (whiteMinorsDeveloped < 3)
                    score -= EARLY_QUEEN_PENALTY * (3 - whiteMinorsDeveloped);
            }

            if (blackQueen != 0 && (blackQueen & 0x0800000000000000UL) == 0) // Queen moved from d8
            {
                if (blackMinorsDeveloped < 3)
                    score += EARLY_QUEEN_PENALTY * (3 - blackMinorsDeveloped);
            }

            return score;
        }

        // 6. Evaluate rook connection
        private static int EvaluateRookConnection(Position position)
        {
            int score = 0;

            var whiteRooks = position.BitboardOf(Color.White, PieceType.Rook);
            var blackRooks = position.BitboardOf(Color.Black, PieceType.Rook);

            // Check if rooks are connected (on same rank with no pieces between)
            if (Bitboard.PopCount(whiteRooks) == 2)
            {
                var rook1 = Bitboard.Bsf(whiteRooks);
                var rook2 = Bitboard.Bsf(whiteRooks & ~Bitboard.SQUARE_BB[(int)rook1]);

                if (Types.RankOf(rook1) == Types.RankOf(rook2))
                {
                    var between = Tables.SQUARES_BETWEEN_BB[(int)rook1][(int)rook2];
                    var allPieces = position.AllPieces(Color.White) | position.AllPieces(Color.Black);

                    if ((between & allPieces) == 0)
                        score += ROOK_CONNECTION_BONUS;
                }
            }

            if (Bitboard.PopCount(blackRooks) == 2)
            {
                var rook1 = Bitboard.Bsf(blackRooks);
                var rook2 = Bitboard.Bsf(blackRooks & ~Bitboard.SQUARE_BB[(int)rook1]);

                if (Types.RankOf(rook1) == Types.RankOf(rook2))
                {
                    var between = Tables.SQUARES_BETWEEN_BB[(int)rook1][(int)rook2];
                    var allPieces = position.AllPieces(Color.White) | position.AllPieces(Color.Black);

                    if ((between & allPieces) == 0)
                        score -= ROOK_CONNECTION_BONUS;
                }
            }

            // Bonus for rooks on open files
            for (int file = 0; file < 8; file++)
            {
                var fileMask = Bitboard.MASK_FILE[file];
                var pawnsOnFile = (position.BitboardOf(Color.White, PieceType.Pawn) |
                                  position.BitboardOf(Color.Black, PieceType.Pawn)) & fileMask;

                if (pawnsOnFile == 0) // Open file
                {
                    if ((whiteRooks & fileMask) != 0)
                        score += 20;
                    if ((blackRooks & fileMask) != 0)
                        score -= 20;
                }
                else if (Bitboard.PopCount(pawnsOnFile) == 1) // Semi-open file
                {
                    if ((whiteRooks & fileMask) != 0 && (position.BitboardOf(Color.White, PieceType.Pawn) & fileMask) == 0)
                        score += 10;
                    if ((blackRooks & fileMask) != 0 && (position.BitboardOf(Color.Black, PieceType.Pawn) & fileMask) == 0)
                        score -= 10;
                }
            }

            return score;
        }

        // 7 & 8. Enhanced king safety evaluation
        private static int EvaluateKingSafety(Position position, int phase)
        {
            int score = 0;

            var whiteKing = Bitboard.Bsf(position.BitboardOf(Color.White, PieceType.King));
            var blackKing = Bitboard.Bsf(position.BitboardOf(Color.Black, PieceType.King));

            // Castling bonus (higher in middlegame)
            var castlingBonus = (CASTLING_BONUS * phase) / 256;

            // Check if kings have castled
            if (whiteKing == Square.g1 || whiteKing == Square.c1)
            {
                score += castlingBonus;

                // Pawn shield evaluation
                if (whiteKing == Square.g1)
                {
                    var shield = position.BitboardOf(Color.White, PieceType.Pawn) & 0xe000UL; // f2, g2, h2
                    score += Bitboard.PopCount(shield) * 10;
                }
                else if (whiteKing == Square.c1)
                {
                    var shield = position.BitboardOf(Color.White, PieceType.Pawn) & 0x700UL; // a2, b2, c2
                    score += Bitboard.PopCount(shield) * 10;
                }
            }

            if (blackKing == Square.g8 || blackKing == Square.c8)
            {
                score -= castlingBonus;

                // Pawn shield evaluation
                if (blackKing == Square.g8)
                {
                    var shield = position.BitboardOf(Color.Black, PieceType.Pawn) & 0xe0000000000000UL; // f7, g7, h7
                    score -= Bitboard.PopCount(shield) * 10;
                }
                else if (blackKing == Square.c8)
                {
                    var shield = position.BitboardOf(Color.Black, PieceType.Pawn) & 0x7000000000000UL; // a7, b7, c7
                    score -= Bitboard.PopCount(shield) * 10;
                }
            }

            // King exposure penalty (more important in middlegame)
            var allPieces = position.AllPieces(Color.White) | position.AllPieces(Color.Black);

            // Count attacking pieces near king
            var whiteKingZone = Tables.Attacks(PieceType.King, whiteKing, 0) | Bitboard.SQUARE_BB[(int)whiteKing];
            var blackKingZone = Tables.Attacks(PieceType.King, blackKing, 0) | Bitboard.SQUARE_BB[(int)blackKing];

            // Enemy pieces attacking king zone
            var whiteAttackers = CountAttackers(position, Color.Black, whiteKingZone, allPieces);
            var blackAttackers = CountAttackers(position, Color.White, blackKingZone, allPieces);

            score -= (whiteAttackers * KING_SAFETY_WEIGHT * phase) / 256;
            score += (blackAttackers * KING_SAFETY_WEIGHT * phase) / 256;

            return score;
        }

        // 9. Additional interesting evaluations
        private static int EvaluatePiecePlacement(Position position)
        {
            int score = 0;

            // Knight outposts
            const ulong WHITE_OUTPOSTS = 0x00003C3C3C000000UL; // Ranks 4-6, files c-f
            const ulong BLACK_OUTPOSTS = 0x0000003C3C3C0000UL; // Ranks 3-5, files c-f

            var whiteKnights = position.BitboardOf(Color.White, PieceType.Knight);
            var blackKnights = position.BitboardOf(Color.Black, PieceType.Knight);
            var enemyPawnAttacks = Tables.PawnAttacks(Color.Black, position.BitboardOf(Color.Black, PieceType.Pawn));
            var friendlyPawnAttacks = Tables.PawnAttacks(Color.White, position.BitboardOf(Color.White, PieceType.Pawn));

            // Knights on outposts that can't be attacked by enemy pawns
            score += Bitboard.PopCount(whiteKnights & WHITE_OUTPOSTS & ~enemyPawnAttacks) * 20;
            score -= Bitboard.PopCount(blackKnights & BLACK_OUTPOSTS & ~friendlyPawnAttacks) * 20;

            // Bad bishops (trapped by own pawns)
            var whiteBishops = position.BitboardOf(Color.White, PieceType.Bishop);
            var blackBishops = position.BitboardOf(Color.Black, PieceType.Bishop);

            // Light squared bishops
            var lightSquares = 0x55AA55AA55AA55AAUL;
            var darkSquares = ~lightSquares;

            var whiteLightBishops = whiteBishops & lightSquares;
            var whiteDarkBishops = whiteBishops & darkSquares;
            var blackLightBishops = blackBishops & lightSquares;
            var blackDarkBishops = blackBishops & darkSquares;

            // Penalty for bishops blocked by own pawns
            var whitePawns = position.BitboardOf(Color.White, PieceType.Pawn);
            var blackPawns = position.BitboardOf(Color.Black, PieceType.Pawn);

            if (whiteLightBishops != 0)
            {
                var blockedPawns = Bitboard.PopCount(whitePawns & lightSquares);
                score -= blockedPawns * 3;
            }
            if (whiteDarkBishops != 0)
            {
                var blockedPawns = Bitboard.PopCount(whitePawns & darkSquares);
                score -= blockedPawns * 3;
            }

            if (blackLightBishops != 0)
            {
                var blockedPawns = Bitboard.PopCount(blackPawns & lightSquares);
                score += blockedPawns * 3;
            }
            if (blackDarkBishops != 0)
            {
                var blockedPawns = Bitboard.PopCount(blackPawns & darkSquares);
                score += blockedPawns * 3;
            }

            return score;
        }

        private static int EvaluatePassedPawns(Position position)
        {
            int score = 0;

            var whitePawns = position.BitboardOf(Color.White, PieceType.Pawn);
            var blackPawns = position.BitboardOf(Color.Black, PieceType.Pawn);

            // Evaluate each white pawn
            var wpawns = whitePawns;
            while (wpawns != 0)
            {
                var pawn = Bitboard.PopLsb(ref wpawns);
                var file = Types.FileOf(pawn);
                var rank = Types.RankOf(pawn);

                // Check if pawn is passed
                ulong frontSpan = 0;
                for (var r = rank + 1; r <= Rank.Rank8; r++)
                {
                    frontSpan |= Bitboard.SQUARE_BB[(int)Types.CreateSquare(file, r)];
                    if (file > 0)
                        frontSpan |= Bitboard.SQUARE_BB[(int)Types.CreateSquare((Move.File)(file - 1), r)];
                    if (file < Move.File.FileH)
                        frontSpan |= Bitboard.SQUARE_BB[(int)Types.CreateSquare((Move.File)(file + 1), r)];
                }

                if ((frontSpan & blackPawns) == 0)
                {
                    // Passed pawn bonus based on rank
                    int bonus = (int)rank * (int)rank * 10;
                    score += bonus;

                    // Additional bonus if supported by own pawn
                    var support = Tables.PawnAttacks(Color.Black, pawn) & whitePawns;
                    if (support != 0)
                        score += bonus / 2;
                }
            }

            // Evaluate each black pawn
            var bpawns = blackPawns;
            while (bpawns != 0)
            {
                var pawn = Bitboard.PopLsb(ref bpawns);
                var file = Types.FileOf(pawn);
                var rank = Types.RankOf(pawn);

                // Check if pawn is passed
                ulong frontSpan = 0;
                for (var r = rank - 1; r >= Rank.Rank1; r--)
                {
                    frontSpan |= Bitboard.SQUARE_BB[(int)Types.CreateSquare(file, r)];
                    if (file > 0)
                        frontSpan |= Bitboard.SQUARE_BB[(int)Types.CreateSquare((Move.File)(file - 1), r)];
                    if (file < Move.File.FileH)
                        frontSpan |= Bitboard.SQUARE_BB[(int)Types.CreateSquare((Move.File)(file + 1), r)];
                }

                if ((frontSpan & whitePawns) == 0)
                {
                    // Passed pawn bonus based on rank (inverted for black)
                    int bonus = (7 - (int)rank) * (7 - (int)rank) * 10;
                    score -= bonus;

                    // Additional bonus if supported by own pawn
                    var support = Tables.PawnAttacks(Color.White, pawn) & blackPawns;
                    if (support != 0)
                        score -= bonus / 2;
                }
            }

            return score;
        }

        private static int EvaluateBishopMobility(Position position)
        {
            int score = 0;
            var allPieces = position.AllPieces(Color.White) | position.AllPieces(Color.Black);

            // White bishops
            var whiteBishops = position.BitboardOf(Color.White, PieceType.Bishop);
            while (whiteBishops != 0)
            {
                var bishop = Bitboard.PopLsb(ref whiteBishops);
                var attacks = Tables.Attacks(PieceType.Bishop, bishop, allPieces);
                var mobility = Bitboard.PopCount(attacks & ~position.AllPieces(Color.White));
                score += mobility * 4;
            }

            // Black bishops
            var blackBishops = position.BitboardOf(Color.Black, PieceType.Bishop);
            while (blackBishops != 0)
            {
                var bishop = Bitboard.PopLsb(ref blackBishops);
                var attacks = Tables.Attacks(PieceType.Bishop, bishop, allPieces);
                var mobility = Bitboard.PopCount(attacks & ~position.AllPieces(Color.Black));
                score -= mobility * 4;
            }

            return score;
        }

        private static int CountAttackers(Position position, Color attackingColor, ulong targetZone, ulong allPieces)
        {
            int attackers = 0;

            // Pawn attacks
            var pawnAttacks = Tables.PawnAttacks(attackingColor, position.BitboardOf(attackingColor, PieceType.Pawn));
            attackers += Bitboard.PopCount(pawnAttacks & targetZone);

            // Knight attacks
            var knights = position.BitboardOf(attackingColor, PieceType.Knight);
            while (knights != 0)
            {
                var knight = Bitboard.PopLsb(ref knights);
                if ((Tables.Attacks(PieceType.Knight, knight, allPieces) & targetZone) != 0)
                    attackers++;
            }

            // Bishop/Queen attacks
            var diagSliders = position.DiagonalSliders(attackingColor);
            while (diagSliders != 0)
            {
                var slider = Bitboard.PopLsb(ref diagSliders);
                if ((Tables.Attacks(PieceType.Bishop, slider, allPieces) & targetZone) != 0)
                    attackers++;
            }

            // Rook/Queen attacks
            var orthSliders = position.OrthogonalSliders(attackingColor);
            while (orthSliders != 0)
            {
                var slider = Bitboard.PopLsb(ref orthSliders);
                if ((Tables.Attacks(PieceType.Rook, slider, allPieces) & targetZone) != 0)
                    attackers++;
            }

            return attackers;
        }
    }
}