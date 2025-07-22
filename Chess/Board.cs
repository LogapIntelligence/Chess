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
    public readonly bool IsSquareAttacked(int square, Color byColor)
    {
        // Add bounds checking
        if (square < 0 || square >= 64) return false;

        ulong occupancy = AllPieces;

        if (byColor == Color.White)
        {
            // Pawn attacks
            if ((BitboardConstants.BlackPawnAttacks[square] & WhitePawns) != 0) return true;

            // Knight attacks
            if ((BitboardConstants.KnightMoves[square] & WhiteKnights) != 0) return true;

            // King attacks
            if ((BitboardConstants.KingMoves[square] & WhiteKing) != 0) return true;

            // Bishop/Queen attacks
            ulong bishopAttacks = MagicBitboards.GetBishopAttacks(square, occupancy);
            if ((bishopAttacks & (WhiteBishops | WhiteQueens)) != 0) return true;

            // Rook/Queen attacks
            ulong rookAttacks = MagicBitboards.GetRookAttacks(square, occupancy);
            if ((rookAttacks & (WhiteRooks | WhiteQueens)) != 0) return true;
        }
        else
        {
            // Pawn attacks
            if ((BitboardConstants.WhitePawnAttacks[square] & BlackPawns) != 0) return true;

            // Knight attacks
            if ((BitboardConstants.KnightMoves[square] & BlackKnights) != 0) return true;

            // King attacks
            if ((BitboardConstants.KingMoves[square] & BlackKing) != 0) return true;

            // Bishop/Queen attacks
            ulong bishopAttacks = MagicBitboards.GetBishopAttacks(square, occupancy);
            if ((bishopAttacks & (BlackBishops | BlackQueens)) != 0) return true;

            // Rook/Queen attacks
            ulong rookAttacks = MagicBitboards.GetRookAttacks(square, occupancy);
            if ((rookAttacks & (BlackRooks | BlackQueens)) != 0) return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsInCheck()
    {
        ulong kingBitboard = SideToMove == Color.White ? WhiteKing : BlackKing;

        // Safety check - should never happen in a valid position
        if (kingBitboard == 0) return false;

        int kingSquare = BitboardConstants.BitScanForward(kingBitboard);
        return IsSquareAttacked(kingSquare, SideToMove == Color.White ? Color.Black : Color.White);
    }

    public void MakeMove(Move move)
    {
        int from = move.From;
        int to = move.To;
        var (piece, color) = GetPieceAt(from);

        // Remove piece from source square
        ulong fromBit = 1UL << from;
        ulong toBit = 1UL << to;
        ulong fromToBit = fromBit | toBit;

        // Handle captures
        if (move.IsCapture && !move.IsEnPassant)
        {
            var (capturedPiece, capturedColor) = GetPieceAt(to);
            ulong capturedBitboard = GetPieceBitboard(capturedPiece, capturedColor);
            SetPieceBitboard(capturedPiece, capturedColor, capturedBitboard & ~toBit);
        }

        // Move the piece
        ulong pieceBitboard = GetPieceBitboard(piece, color);
        SetPieceBitboard(piece, color, pieceBitboard ^ fromToBit);

        if (move.IsCapture && !move.IsEnPassant)
        {
            // Check if we're capturing a rook on its starting square
            if (to == 0) CastlingRights &= ~CastlingRights.WhiteQueenside;
            else if (to == 7) CastlingRights &= ~CastlingRights.WhiteKingside;
            else if (to == 56) CastlingRights &= ~CastlingRights.BlackQueenside;
            else if (to == 63) CastlingRights &= ~CastlingRights.BlackKingside;
        }

        // Handle special moves
        if (move.IsEnPassant)
        {
            int captureSquare = to + (color == Color.White ? -8 : 8);
            ulong captureBit = 1UL << captureSquare;
            if (color == Color.White)
                BlackPawns &= ~captureBit;
            else
                WhitePawns &= ~captureBit;
        }
        else if (move.IsCastling)
        {
            // Move the rook
            if (to > from) // Kingside
            {
                int rookFrom = to + 1;
                int rookTo = to - 1;
                ulong rookFromToBit = 1UL << rookFrom | 1UL << rookTo;
                if (color == Color.White)
                    WhiteRooks ^= rookFromToBit;
                else
                    BlackRooks ^= rookFromToBit;
            }
            else // Queenside
            {
                int rookFrom = to - 2;
                int rookTo = to + 1;
                ulong rookFromToBit = 1UL << rookFrom | 1UL << rookTo;
                if (color == Color.White)
                    WhiteRooks ^= rookFromToBit;
                else
                    BlackRooks ^= rookFromToBit;
            }
        }
        else if (move.IsPromotion)
        {
            // Remove pawn and add promoted piece
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

        // Update castling rights
        if (piece == PieceType.King)
        {
            if (color == Color.White)
                CastlingRights &= ~(CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside);
            else
                CastlingRights &= ~(CastlingRights.BlackKingside | CastlingRights.BlackQueenside);
        }
        else if (piece == PieceType.Rook)
        {
            if (from == 0) CastlingRights &= ~CastlingRights.WhiteQueenside;
            else if (from == 7) CastlingRights &= ~CastlingRights.WhiteKingside;
            else if (from == 56) CastlingRights &= ~CastlingRights.BlackQueenside;
            else if (from == 63) CastlingRights &= ~CastlingRights.BlackKingside;
        }

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
        SideToMove = SideToMove == Color.White ? Color.Black : Color.White;

        // Update aggregate bitboards
        UpdateAggregateBitboards();
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