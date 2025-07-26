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
            -30,  5, 10, 15, 15, 10,  5,-30,
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

        public static int Evaluate(Position position)
        {
            int score = 0;

            // Material and positional evaluation for both sides
            score += EvaluateSide(position, Color.White);
            score -= EvaluateSide(position, Color.Black);

            // Additional positional factors
            score += EvaluateMobility(position);
            score += EvaluateKingSafety(position);
            score += EvaluatePawnStructure(position);

            return position.Turn == Color.White ? score : -score;
        }

        private static int EvaluateSide(Position position, Color color)
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

            // King
            var king = position.BitboardOf(color, PieceType.King);
            score += EvaluatePieceSquares(king, KingMiddleGameTable, color);

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

        private static int EvaluateMobility(Position position)
        {
            // Simplified mobility based on piece development
            int score = 0;

            // Count developed pieces (not on starting squares)
            var whiteKnights = position.BitboardOf(Color.White, PieceType.Knight);
            var blackKnights = position.BitboardOf(Color.Black, PieceType.Knight);

            // Knights not on starting squares
            score += Bitboard.PopCount(whiteKnights & ~0x42UL) * 5; // Not on b1/g1
            score -= Bitboard.PopCount(blackKnights & ~0x4200000000000000UL) * 5; // Not on b8/g8

            var whiteBishops = position.BitboardOf(Color.White, PieceType.Bishop);
            var blackBishops = position.BitboardOf(Color.Black, PieceType.Bishop);

            // Bishops not on starting squares
            score += Bitboard.PopCount(whiteBishops & ~0x24UL) * 5; // Not on c1/f1
            score -= Bitboard.PopCount(blackBishops & ~0x2400000000000000UL) * 5; // Not on c8/f8

            return score;
        }

        private static int EvaluateKingSafety(Position position)
        {
            int score = 0;

            // Penalize king exposure (simplified)
            var whiteKing = Bitboard.Bsf(position.BitboardOf(Color.White, PieceType.King));
            var blackKing = Bitboard.Bsf(position.BitboardOf(Color.Black, PieceType.King));

            // Prefer castled king positions in opening/middlegame
            if (Types.FileOf(whiteKing) == Move.File.FileG || Types.FileOf(whiteKing) == Move.File.FileC)
                score += 30;

            if (Types.FileOf(blackKing) == Move.File.FileG || Types.FileOf(blackKing) == Move.File.FileC)
                score -= 30;

            return score;
        }

        private static int EvaluatePawnStructure(Position position)
        {
            int score = 0;

            // Evaluate pawn structure
            var whitePawns = position.BitboardOf(Color.White, PieceType.Pawn);
            var blackPawns = position.BitboardOf(Color.Black, PieceType.Pawn);

            // Doubled pawns penalty
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

            return score;
        }
    }
}