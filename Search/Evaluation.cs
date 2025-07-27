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
        // Values are in centipawns
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
            int materialWhite = 0;
            int materialBlack = 0;

            // Evaluate each piece type
            score += EvaluatePieceType(position, PieceType.Pawn, PAWN_VALUE, PAWN_PST);
            score += EvaluatePieceType(position, PieceType.Knight, KNIGHT_VALUE, KNIGHT_PST);
            score += EvaluatePieceType(position, PieceType.Bishop, BISHOP_VALUE, BISHOP_PST);
            score += EvaluatePieceType(position, PieceType.Rook, ROOK_VALUE, ROOK_PST);
            score += EvaluatePieceType(position, PieceType.Queen, QUEEN_VALUE, QUEEN_PST);
            score += EvaluatePieceType(position, PieceType.King, 0, KING_MIDDLEGAME_PST);

            // Count material for endgame detection (simplified)
            materialWhite = Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Pawn)) * PAWN_VALUE +
                           Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Knight)) * KNIGHT_VALUE +
                           Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Bishop)) * BISHOP_VALUE +
                           Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Rook)) * ROOK_VALUE +
                           Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Queen)) * QUEEN_VALUE;

            materialBlack = Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Pawn)) * PAWN_VALUE +
                           Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Knight)) * KNIGHT_VALUE +
                           Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Bishop)) * BISHOP_VALUE +
                           Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Rook)) * ROOK_VALUE +
                           Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Queen)) * QUEEN_VALUE;

            // Add basic mobility bonus (simplified)
            score += EvaluateMobility(position);

            // Return score from the perspective of the side to move
            return position.Turn == Color.White ? score : -score;
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

            // Very simplified mobility evaluation
            // Count the number of pseudo-legal moves available

            // This is a rough approximation - in a real engine you'd calculate actual mobility
            ulong occupied = position.AllPieces(Color.White) | position.AllPieces(Color.Black);

            // Knight mobility
            ulong whiteKnights = position.BitboardOf(Color.White, PieceType.Knight);
            ulong blackKnights = position.BitboardOf(Color.Black, PieceType.Knight);

            while (whiteKnights != 0)
            {
                Square sq = Bitboard.PopLsb(ref whiteKnights);
                score += Bitboard.PopCount(Tables.KNIGHT_ATTACKS[(int)sq] & ~position.AllPieces(Color.White)) * 2;
            }

            while (blackKnights != 0)
            {
                Square sq = Bitboard.PopLsb(ref blackKnights);
                score -= Bitboard.PopCount(Tables.KNIGHT_ATTACKS[(int)sq] & ~position.AllPieces(Color.Black)) * 2;
            }

            return score;
        }
    }
}