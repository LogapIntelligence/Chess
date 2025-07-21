namespace Chess;

using System;
using System.Runtime.CompilerServices;

public static class SimpleEvaluation
{
    // Basic piece values
    private const int PawnValue = 100;
    private const int KnightValue = 320;
    private const int BishopValue = 330;
    private const int RookValue = 500;
    private const int QueenValue = 900;

    // Simple bonuses
    private const int CenterPawnBonus = 20;
    private const int PassedPawnBonus = 30;
    private const int RookOnSeventhBonus = 20;
    private const int BishopPairBonus = 30;
    private const int DoubledPawnPenalty = -15;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Evaluate(ref Board board)
    {
        int score = 0;

        // Material count
        score += CountMaterial(ref board);

        // Positional bonuses
        score += EvaluatePosition(ref board);

        // Return score from side to move perspective
        return board.SideToMove == Color.White ? score : -score;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountMaterial(ref Board board)
    {
        int material = 0;

        // White pieces
        material += BitboardConstants.PopCount(board.WhitePawns) * PawnValue;
        material += BitboardConstants.PopCount(board.WhiteKnights) * KnightValue;
        material += BitboardConstants.PopCount(board.WhiteBishops) * BishopValue;
        material += BitboardConstants.PopCount(board.WhiteRooks) * RookValue;
        material += BitboardConstants.PopCount(board.WhiteQueens) * QueenValue;

        // Black pieces
        material -= BitboardConstants.PopCount(board.BlackPawns) * PawnValue;
        material -= BitboardConstants.PopCount(board.BlackKnights) * KnightValue;
        material -= BitboardConstants.PopCount(board.BlackBishops) * BishopValue;
        material -= BitboardConstants.PopCount(board.BlackRooks) * RookValue;
        material -= BitboardConstants.PopCount(board.BlackQueens) * QueenValue;

        return material;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvaluatePosition(ref Board board)
    {
        int score = 0;

        // Center pawns (e4, d4, e5, d5)
        const ulong centerSquares = (1UL << 27) | (1UL << 28) | (1UL << 35) | (1UL << 36);
        score += BitboardConstants.PopCount(board.WhitePawns & centerSquares) * CenterPawnBonus;
        score -= BitboardConstants.PopCount(board.BlackPawns & centerSquares) * CenterPawnBonus;

        // Rooks on 7th rank
        const ulong rank7 = BitboardConstants.Rank7;
        const ulong rank2 = BitboardConstants.Rank2;
        score += BitboardConstants.PopCount(board.WhiteRooks & rank7) * RookOnSeventhBonus;
        score -= BitboardConstants.PopCount(board.BlackRooks & rank2) * RookOnSeventhBonus;

        // Bishop pair
        if (BitboardConstants.PopCount(board.WhiteBishops) >= 2)
            score += BishopPairBonus;
        if (BitboardConstants.PopCount(board.BlackBishops) >= 2)
            score -= BishopPairBonus;

        // Simple pawn structure
        score += EvaluatePawns(board.WhitePawns, board.BlackPawns, true);
        score -= EvaluatePawns(board.BlackPawns, board.WhitePawns, false);

        return score;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvaluatePawns(ulong friendlyPawns, ulong enemyPawns, bool isWhite)
    {
        int score = 0;

        // Check each file for doubled pawns and passed pawns
        for (int file = 0; file < 8; file++)
        {
            ulong fileMask = BitboardConstants.FileA << file;
            ulong pawnsOnFile = friendlyPawns & fileMask;
            int pawnCount = BitboardConstants.PopCount(pawnsOnFile);

            // Doubled pawns
            if (pawnCount > 1)
                score += DoubledPawnPenalty * (pawnCount - 1);

            // Simple passed pawn detection
            if (pawnCount > 0)
            {
                // For black, we want the most advanced pawn (highest bit)
                int pawnSquare = isWhite
                    ? BitboardConstants.BitScanForward(pawnsOnFile)
                    : BitboardConstants.BitScanReverse(pawnsOnFile);

                int rank = pawnSquare / 8;
                if (isWhite)
                {
                    // Check if no enemy pawns in front
                    ulong frontMask = 0;
                    for (int r = rank + 1; r < 8; r++)
                        frontMask |= 1UL << (r * 8 + file);

                    if ((frontMask & enemyPawns) == 0)
                        score += PassedPawnBonus * (rank - 1); // Bonus increases with rank
                }
                else
                {
                    // For black, check ranks below
                    ulong frontMask = 0;
                    for (int r = rank - 1; r >= 0; r--)
                        frontMask |= 1UL << (r * 8 + file);

                    if ((frontMask & enemyPawns) == 0)
                        score += PassedPawnBonus * (6 - rank);
                }
            }
        }

        return score;
    }
}