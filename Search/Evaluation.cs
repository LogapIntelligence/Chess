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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Evaluate(Position position)
        {
            int score = 0;

            // White material
            score += Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Pawn)) * PAWN_VALUE;
            score += Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Knight)) * KNIGHT_VALUE;
            score += Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Bishop)) * BISHOP_VALUE;
            score += Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Rook)) * ROOK_VALUE;
            score += Bitboard.PopCount(position.BitboardOf(Color.White, PieceType.Queen)) * QUEEN_VALUE;

            // Black material
            score -= Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Pawn)) * PAWN_VALUE;
            score -= Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Knight)) * KNIGHT_VALUE;
            score -= Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Bishop)) * BISHOP_VALUE;
            score -= Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Rook)) * ROOK_VALUE;
            score -= Bitboard.PopCount(position.BitboardOf(Color.Black, PieceType.Queen)) * QUEEN_VALUE;

            return position.Turn == Color.White ? score : -score;
        }
    }
}