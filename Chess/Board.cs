namespace Chess;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct Board
{
    // Bitboards for each piece type and color
    public ulong WhitePawns;
    public ulong WhiteKnights;
    public ulong WhiteBishops;
    public ulong WhiteRooks;
    public ulong WhiteQueens;
    public ulong WhiteKing;
    public ulong BlackPawns;
    public ulong BlackKnights;
    public ulong BlackBishops;
    public ulong BlackRooks;
    public ulong BlackQueens;
    public ulong BlackKing;

    // Aggregate bitboards
    public ulong WhitePieces;
    public ulong BlackPieces;
    public ulong AllPieces;

    // Game state
    public Color SideToMove;
    public CastlingRights CastlingRights;
    public int EnPassantSquare; // -1 if none
    public int HalfmoveClock;
    public int FullmoveNumber;

    public static Board StartingPosition()
    {
        var board = new Board
        {
            WhitePawns = 0x000000000000FF00UL,
            WhiteKnights = 0x0000000000000042UL,
            WhiteBishops = 0x0000000000000024UL,
            WhiteRooks = 0x0000000000000081UL,
            WhiteQueens = 0x0000000000000008UL,
            WhiteKing = 0x0000000000000010UL,

            BlackPawns = 0x00FF000000000000UL,
            BlackKnights = 0x4200000000000000UL,
            BlackBishops = 0x2400000000000000UL,
            BlackRooks = 0x8100000000000000UL,
            BlackQueens = 0x0800000000000000UL,
            BlackKing = 0x1000000000000000UL,

            SideToMove = Color.White,
            CastlingRights = CastlingRights.All,
            EnPassantSquare = -1,
            HalfmoveClock = 0,
            FullmoveNumber = 1
        };

        board.UpdateAggregateBitboards();
        return board;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateAggregateBitboards()
    {
        WhitePieces = WhitePawns | WhiteKnights | WhiteBishops | WhiteRooks | WhiteQueens | WhiteKing;
        BlackPieces = BlackPawns | BlackKnights | BlackBishops | BlackRooks | BlackQueens | BlackKing;
        AllPieces = WhitePieces | BlackPieces;
    }

    public readonly Board Clone()
    {
        return this; // This works because structs are value types
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ulong GetPieceBitboard(PieceType piece, Color color)
    {
        return color == Color.White ? piece switch
        {
            PieceType.Pawn => WhitePawns,
            PieceType.Knight => WhiteKnights,
            PieceType.Bishop => WhiteBishops,
            PieceType.Rook => WhiteRooks,
            PieceType.Queen => WhiteQueens,
            PieceType.King => WhiteKing,
            _ => 0
        } : piece switch
        {
            PieceType.Pawn => BlackPawns,
            PieceType.Knight => BlackKnights,
            PieceType.Bishop => BlackBishops,
            PieceType.Rook => BlackRooks,
            PieceType.Queen => BlackQueens,
            PieceType.King => BlackKing,
            _ => 0
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPieceBitboard(PieceType piece, Color color, ulong bitboard)
    {
        if (color == Color.White)
        {
            switch (piece)
            {
                case PieceType.Pawn: WhitePawns = bitboard; break;
                case PieceType.Knight: WhiteKnights = bitboard; break;
                case PieceType.Bishop: WhiteBishops = bitboard; break;
                case PieceType.Rook: WhiteRooks = bitboard; break;
                case PieceType.Queen: WhiteQueens = bitboard; break;
                case PieceType.King: WhiteKing = bitboard; break;
            }
        }
        else
        {
            switch (piece)
            {
                case PieceType.Pawn: BlackPawns = bitboard; break;
                case PieceType.Knight: BlackKnights = bitboard; break;
                case PieceType.Bishop: BlackBishops = bitboard; break;
                case PieceType.Rook: BlackRooks = bitboard; break;
                case PieceType.Queen: BlackQueens = bitboard; break;
                case PieceType.King: BlackKing = bitboard; break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly (PieceType piece, Color color) GetPieceAt(int square)
    {
        ulong bit = 1UL << square;

        if ((WhitePieces & bit) != 0)
        {
            if ((WhitePawns & bit) != 0) return (PieceType.Pawn, Color.White);
            if ((WhiteKnights & bit) != 0) return (PieceType.Knight, Color.White);
            if ((WhiteBishops & bit) != 0) return (PieceType.Bishop, Color.White);
            if ((WhiteRooks & bit) != 0) return (PieceType.Rook, Color.White);
            if ((WhiteQueens & bit) != 0) return (PieceType.Queen, Color.White);
            if ((WhiteKing & bit) != 0) return (PieceType.King, Color.White);
        }
        else if ((BlackPieces & bit) != 0)
        {
            if ((BlackPawns & bit) != 0) return (PieceType.Pawn, Color.Black);
            if ((BlackKnights & bit) != 0) return (PieceType.Knight, Color.Black);
            if ((BlackBishops & bit) != 0) return (PieceType.Bishop, Color.Black);
            if ((BlackRooks & bit) != 0) return (PieceType.Rook, Color.Black);
            if ((BlackQueens & bit) != 0) return (PieceType.Queen, Color.Black);
            if ((BlackKing & bit) != 0) return (PieceType.King, Color.Black);
        }

        return (PieceType.None, Color.White);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool HasNonPawnMaterial()
    {
        if (SideToMove == Color.White)
        {
            return WhiteKnights != 0 || WhiteBishops != 0 ||
                   WhiteRooks != 0 || WhiteQueens != 0;
        }
        else
        {
            return BlackKnights != 0 || BlackBishops != 0 ||
                   BlackRooks != 0 || BlackQueens != 0;
        }
    }
    private int GetNullMoveReduction(int depth)
    {
        // More aggressive reduction at higher depths
        if (depth >= 8) return 4;
        if (depth >= 5) return 3;
        return 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsSquareAttacked(int square, Color byColor)
    {
        // Validate square
        if ((uint)square >= 64) return false;

        ulong occupancy = AllPieces;

        if (byColor == Color.White)
        {
            // Check pawn attacks first (most common)
            if ((BitboardConstants.BlackPawnAttacks[square] & WhitePawns) != 0)
                return true;

            // Knight attacks
            if ((BitboardConstants.KnightMoves[square] & WhiteKnights) != 0)
                return true;

            // King attacks
            if ((BitboardConstants.KingMoves[square] & WhiteKing) != 0)
                return true;

            // Sliding pieces - combine similar attack patterns
            ulong queens = WhiteQueens;

            if (queens != 0 || WhiteBishops != 0)
            {
                ulong bishopAttacks = MagicBitboards.GetBishopAttacks(square, occupancy);
                if ((bishopAttacks & (WhiteBishops | queens)) != 0)
                    return true;
            }

            if (queens != 0 || WhiteRooks != 0)
            {
                ulong rookAttacks = MagicBitboards.GetRookAttacks(square, occupancy);
                if ((rookAttacks & (WhiteRooks | queens)) != 0)
                    return true;
            }
        }
        else
        {
            // Check pawn attacks first (most common)
            if ((BitboardConstants.WhitePawnAttacks[square] & BlackPawns) != 0)
                return true;

            // Knight attacks
            if ((BitboardConstants.KnightMoves[square] & BlackKnights) != 0)
                return true;

            // King attacks
            if ((BitboardConstants.KingMoves[square] & BlackKing) != 0)
                return true;

            // Sliding pieces - combine similar attack patterns
            ulong queens = BlackQueens;

            if (queens != 0 || BlackBishops != 0)
            {
                ulong bishopAttacks = MagicBitboards.GetBishopAttacks(square, occupancy);
                if ((bishopAttacks & (BlackBishops | queens)) != 0)
                    return true;
            }

            if (queens != 0 || BlackRooks != 0)
            {
                ulong rookAttacks = MagicBitboards.GetRookAttacks(square, occupancy);
                if ((rookAttacks & (BlackRooks | queens)) != 0)
                    return true;
            }
        }

        return false;
    }
    static int[] index64 =
    {
            0, 47,  1, 56, 48, 27,  2, 60,
           57, 49, 41, 37, 28, 16,  3, 61,
           54, 58, 35, 52, 50, 42, 21, 44,
           38, 32, 29, 23, 17, 11,  4, 62,
           46, 55, 26, 59, 40, 36, 15, 53,
           34, 51, 20, 43, 31, 22, 10, 45,
           25, 39, 14, 33, 19, 30,  9, 24,
           13, 18,  8, 12,  7,  6,  5, 63
     };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsInCheckFast()
    {
        try
        {


            // Get king position without PopCount
            ulong kingBB = SideToMove == Color.White ? WhiteKing : BlackKing;

            // Use De Bruijn sequence for fast bit scan
            const ulong debruijn64 = 0x03f79d71b4cb0a89UL;


            int kingSquare = index64[((kingBB ^ (kingBB - 1)) * debruijn64) >> 58];

            return IsSquareAttacked(kingSquare, SideToMove == Color.White ? Color.Black : Color.White);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Update castling rights with lookup table
    static readonly byte[] CastlingRightsMask = new byte[64]
    {
        0b1101, 0xFF, 0xFF, 0xFF, 0b1100, 0xFF, 0xFF, 0b1110, // Rank 1
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,         // Rank 2
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,         // Rank 3
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,         // Rank 4
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,         // Rank 5
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,         // Rank 6
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,         // Rank 7
        0b0111, 0xFF, 0xFF, 0xFF, 0b0011, 0xFF, 0xFF, 0b1011   // Rank 8
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MakeMove(Move move)
    {
        try
        {


            int from = move.From;
            int to = move.To;

            // Use a single GetPieceAt call and cache the result
            ulong fromBit = 1UL << from;
            ulong toBit = 1UL << to;
            ulong fromToBit = fromBit | toBit;

            // Determine piece type and color more efficiently
            PieceType piece;
            Color color = SideToMove;

            // Quick piece identification using bit operations
            if (color == Color.White)
            {
                if ((WhitePawns & fromBit) != 0) piece = PieceType.Pawn;
                else if ((WhiteKnights & fromBit) != 0) piece = PieceType.Knight;
                else if ((WhiteBishops & fromBit) != 0) piece = PieceType.Bishop;
                else if ((WhiteRooks & fromBit) != 0) piece = PieceType.Rook;
                else if ((WhiteQueens & fromBit) != 0) piece = PieceType.Queen;
                else piece = PieceType.King;
            }
            else
            {
                if ((BlackPawns & fromBit) != 0) piece = PieceType.Pawn;
                else if ((BlackKnights & fromBit) != 0) piece = PieceType.Knight;
                else if ((BlackBishops & fromBit) != 0) piece = PieceType.Bishop;
                else if ((BlackRooks & fromBit) != 0) piece = PieceType.Rook;
                else if ((BlackQueens & fromBit) != 0) piece = PieceType.Queen;
                else piece = PieceType.King;
            }

            // Handle captures first (most common case after quiet moves)
            if (move.IsCapture && !move.IsEnPassant)
            {
                // Remove captured piece - avoid GetPieceAt call
                if (color == Color.White)
                {
                    if ((BlackPawns & toBit) != 0) BlackPawns &= ~toBit;
                    else if ((BlackKnights & toBit) != 0) BlackKnights &= ~toBit;
                    else if ((BlackBishops & toBit) != 0) BlackBishops &= ~toBit;
                    else if ((BlackRooks & toBit) != 0)
                    {
                        BlackRooks &= ~toBit;
                        // Update castling rights for rook capture
                        if (to == 56) CastlingRights &= ~CastlingRights.BlackQueenside;
                        else if (to == 63) CastlingRights &= ~CastlingRights.BlackKingside;
                    }
                    else if ((BlackQueens & toBit) != 0) BlackQueens &= ~toBit;
                }
                else
                {
                    if ((WhitePawns & toBit) != 0) WhitePawns &= ~toBit;
                    else if ((WhiteKnights & toBit) != 0) WhiteKnights &= ~toBit;
                    else if ((WhiteBishops & toBit) != 0) WhiteBishops &= ~toBit;
                    else if ((WhiteRooks & toBit) != 0)
                    {
                        WhiteRooks &= ~toBit;
                        // Update castling rights for rook capture
                        if (to == 0) CastlingRights &= ~CastlingRights.WhiteQueenside;
                        else if (to == 7) CastlingRights &= ~CastlingRights.WhiteKingside;
                    }
                    else if ((WhiteQueens & toBit) != 0) WhiteQueens &= ~toBit;
                }
            }

            // Move the piece using XOR (works for both quiet and capture moves)
            if (color == Color.White)
            {
                switch (piece)
                {
                    case PieceType.Pawn: WhitePawns ^= fromToBit; break;
                    case PieceType.Knight: WhiteKnights ^= fromToBit; break;
                    case PieceType.Bishop: WhiteBishops ^= fromToBit; break;
                    case PieceType.Rook: WhiteRooks ^= fromToBit; break;
                    case PieceType.Queen: WhiteQueens ^= fromToBit; break;
                    case PieceType.King: WhiteKing ^= fromToBit; break;
                }
            }
            else
            {
                switch (piece)
                {
                    case PieceType.Pawn: BlackPawns ^= fromToBit; break;
                    case PieceType.Knight: BlackKnights ^= fromToBit; break;
                    case PieceType.Bishop: BlackBishops ^= fromToBit; break;
                    case PieceType.Rook: BlackRooks ^= fromToBit; break;
                    case PieceType.Queen: BlackQueens ^= fromToBit; break;
                    case PieceType.King: BlackKing ^= fromToBit; break;
                }
            }

            // Handle special moves
            if (move.IsPromotion)
            {
                // Remove pawn, add promoted piece
                if (color == Color.White)
                {
                    WhitePawns &= ~toBit;
                    switch (move.Promotion)
                    {
                        case PieceType.Queen: WhiteQueens |= toBit; break;
                        case PieceType.Rook: WhiteRooks |= toBit; break;
                        case PieceType.Bishop: WhiteBishops |= toBit; break;
                        case PieceType.Knight: WhiteKnights |= toBit; break;
                    }
                }
                else
                {
                    BlackPawns &= ~toBit;
                    switch (move.Promotion)
                    {
                        case PieceType.Queen: BlackQueens |= toBit; break;
                        case PieceType.Rook: BlackRooks |= toBit; break;
                        case PieceType.Bishop: BlackBishops |= toBit; break;
                        case PieceType.Knight: BlackKnights |= toBit; break;
                    }
                }
            }
            else if (move.IsEnPassant)
            {
                int captureSquare = to + (color == Color.White ? -8 : 8);
                if (color == Color.White)
                    BlackPawns &= ~(1UL << captureSquare);
                else
                    WhitePawns &= ~(1UL << captureSquare);
            }
            else if (move.IsCastling)
            {
                // Optimized castling - use precomputed rook moves
                if (color == Color.White)
                {
                    if (to == 6) // Kingside
                    {
                        WhiteRooks ^= 0xA0UL; // Move rook from h1 to f1
                    }
                    else // Queenside
                    {
                        WhiteRooks ^= 0x09UL; // Move rook from a1 to d1
                    }
                }
                else
                {
                    if (to == 62) // Kingside
                    {
                        BlackRooks ^= 0xA000000000000000UL; // Move rook from h8 to f8
                    }
                    else // Queenside
                    {
                        BlackRooks ^= 0x0900000000000000UL; // Move rook from a8 to d8
                    }
                }
            }

            CastlingRights &= (CastlingRights)(CastlingRightsMask[from] & CastlingRightsMask[to]);

            // Update en passant square
            EnPassantSquare = move.IsDoublePush ? (from + to) / 2 : -1;

            // Update clocks
            if (piece == PieceType.Pawn || move.IsCapture)
                HalfmoveClock = 0;
            else
                HalfmoveClock++;

            if (SideToMove == Color.Black)
                FullmoveNumber++;

            // Switch side to move
            SideToMove ^= (Color)1; // Flip between 0 and 1

            // Update aggregate bitboards
            UpdateAggregateBitboards();
        }
        catch (Exception)
        {
        }
        }

}

    public enum Color : byte
{
    White = 0,
    Black = 1
}

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingside = 1,
    WhiteQueenside = 2,
    BlackKingside = 4,
    BlackQueenside = 8,
    All = WhiteKingside | WhiteQueenside | BlackKingside | BlackQueenside
}