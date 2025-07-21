namespace Chess;

using System.Runtime.CompilerServices;

public static class BitboardConstants
{
    // File masks
    public const ulong FileA = 0x0101010101010101UL;
    public const ulong FileB = 0x0202020202020202UL;
    public const ulong FileC = 0x0404040404040404UL;
    public const ulong FileD = 0x0808080808080808UL;
    public const ulong FileE = 0x1010101010101010UL;
    public const ulong FileF = 0x2020202020202020UL;
    public const ulong FileG = 0x4040404040404040UL;
    public const ulong FileH = 0x8080808080808080UL;

    // Rank masks
    public const ulong Rank1 = 0x00000000000000FFUL;
    public const ulong Rank2 = 0x000000000000FF00UL;
    public const ulong Rank3 = 0x0000000000FF0000UL;
    public const ulong Rank4 = 0x00000000FF000000UL;
    public const ulong Rank5 = 0x000000FF00000000UL;
    public const ulong Rank6 = 0x0000FF0000000000UL;
    public const ulong Rank7 = 0x00FF000000000000UL;
    public const ulong Rank8 = 0xFF00000000000000UL;

    // Diagonal masks
    public const ulong MainDiagonal = 0x8040201008040201UL;
    public const ulong AntiDiagonal = 0x0102040810204080UL;

    // Castle masks
    public const ulong WhiteKingsideCastleMask = 0x60UL;
    public const ulong WhiteQueensideCastleMask = 0x0EUL;
    public const ulong BlackKingsideCastleMask = 0x6000000000000000UL;
    public const ulong BlackQueensideCastleMask = 0x0E00000000000000UL;

    // Pre-calculated knight moves
    public static readonly ulong[] KnightMoves = new ulong[64];

    // Pre-calculated king moves
    public static readonly ulong[] KingMoves = new ulong[64];

    // Pre-calculated pawn attacks
    public static readonly ulong[] WhitePawnAttacks = new ulong[64];
    public static readonly ulong[] BlackPawnAttacks = new ulong[64];

    static BitboardConstants()
    {
        InitializeKnightMoves();
        InitializeKingMoves();
        InitializePawnAttacks();
    }

    private static void InitializeKnightMoves()
    {
        for (int sq = 0; sq < 64; sq++)
        {
            ulong bitboard = 1UL << sq;
            ulong moves = 0;

            // All 8 knight moves
            if ((bitboard & ~FileA & ~FileB & ~Rank8) != 0) moves |= bitboard << 6;
            if ((bitboard & ~FileA & ~FileB & ~Rank1) != 0) moves |= bitboard >> 10;
            if ((bitboard & ~FileG & ~FileH & ~Rank8) != 0) moves |= bitboard << 10;
            if ((bitboard & ~FileG & ~FileH & ~Rank1) != 0) moves |= bitboard >> 6;
            if ((bitboard & ~FileA & ~Rank7 & ~Rank8) != 0) moves |= bitboard << 15;
            if ((bitboard & ~FileA & ~Rank1 & ~Rank2) != 0) moves |= bitboard >> 17;
            if ((bitboard & ~FileH & ~Rank7 & ~Rank8) != 0) moves |= bitboard << 17;
            if ((bitboard & ~FileH & ~Rank1 & ~Rank2) != 0) moves |= bitboard >> 15;

            KnightMoves[sq] = moves;
        }
    }

    private static void InitializeKingMoves()
    {
        for (int sq = 0; sq < 64; sq++)
        {
            ulong bitboard = 1UL << sq;
            ulong moves = 0;

            if ((bitboard & ~FileA) != 0) moves |= bitboard >> 1;
            if ((bitboard & ~FileH) != 0) moves |= bitboard << 1;
            if ((bitboard & ~Rank1) != 0) moves |= bitboard >> 8;
            if ((bitboard & ~Rank8) != 0) moves |= bitboard << 8;
            if ((bitboard & ~FileA & ~Rank1) != 0) moves |= bitboard >> 9;
            if ((bitboard & ~FileH & ~Rank1) != 0) moves |= bitboard >> 7;
            if ((bitboard & ~FileA & ~Rank8) != 0) moves |= bitboard << 7;
            if ((bitboard & ~FileH & ~Rank8) != 0) moves |= bitboard << 9;

            KingMoves[sq] = moves;
        }
    }

    private static void InitializePawnAttacks()
    {
        for (int sq = 0; sq < 64; sq++)
        {
            ulong bitboard = 1UL << sq;

            // White pawn attacks
            ulong whiteAttacks = 0;
            if ((bitboard & ~FileA & ~Rank8) != 0) whiteAttacks |= bitboard << 7;
            if ((bitboard & ~FileH & ~Rank8) != 0) whiteAttacks |= bitboard << 9;
            WhitePawnAttacks[sq] = whiteAttacks;

            // Black pawn attacks
            ulong blackAttacks = 0;
            if ((bitboard & ~FileA & ~Rank1) != 0) blackAttacks |= bitboard >> 9;
            if ((bitboard & ~FileH & ~Rank1) != 0) blackAttacks |= bitboard >> 7;
            BlackPawnAttacks[sq] = blackAttacks;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopCount(ulong bitboard)
    {
        return System.Numerics.BitOperations.PopCount(bitboard);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BitScanForward(ulong bitboard)
    {
        return System.Numerics.BitOperations.TrailingZeroCount(bitboard);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SetBit(ulong bitboard, int square)
    {
        return bitboard | (1UL << square);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ClearBit(ulong bitboard, int square)
    {
        return bitboard & ~(1UL << square);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetBit(ulong bitboard, int square)
    {
        return (bitboard & (1UL << square)) != 0;
    }
}