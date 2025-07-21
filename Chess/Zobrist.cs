namespace Chess;

using System;
using System.Runtime.CompilerServices;

public static class Zobrist
{
    // Random numbers for each piece on each square
    private static readonly ulong[,,] PieceKeys = new ulong[2, 7, 64]; // [color, piece, square]
    private static readonly ulong[] CastlingKeys = new ulong[16]; // All castling combinations
    private static readonly ulong[] EnPassantKeys = new ulong[8]; // One for each file
    private static readonly ulong SideToMoveKey;

    static Zobrist()
    {
        var rng = new Random(1337); // Fixed seed for reproducibility

        // Initialize piece keys
        for (int color = 0; color < 2; color++)
        {
            for (int piece = 0; piece < 7; piece++)
            {
                for (int square = 0; square < 64; square++)
                {
                    PieceKeys[color, piece, square] = RandomUlong(rng);
                }
            }
        }

        // Initialize castling keys
        for (int i = 0; i < 16; i++)
        {
            CastlingKeys[i] = RandomUlong(rng);
        }

        // Initialize en passant keys
        for (int i = 0; i < 8; i++)
        {
            EnPassantKeys[i] = RandomUlong(rng);
        }

        // Initialize side to move key
        SideToMoveKey = RandomUlong(rng);
    }

    private static ulong RandomUlong(Random rng)
    {
        byte[] buffer = new byte[8];
        rng.NextBytes(buffer);
        return BitConverter.ToUInt64(buffer, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ComputeHash(ref Board board)
    {
        ulong hash = 0;

        // Hash pieces
        for (int square = 0; square < 64; square++)
        {
            var (piece, color) = board.GetPieceAt(square);
            if (piece != PieceType.None)
            {
                hash ^= PieceKeys[(int)color, (int)piece, square];
            }
        }

        // Hash castling rights
        hash ^= CastlingKeys[(int)board.CastlingRights];

        // Hash en passant
        if (board.EnPassantSquare >= 0)
        {
            int file = board.EnPassantSquare % 8;
            hash ^= EnPassantKeys[file];
        }

        // Hash side to move
        if (board.SideToMove == Color.Black)
        {
            hash ^= SideToMoveKey;
        }

        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong UpdateHash(ulong hash, Move move, ref Board board)
    {
        var (piece, color) = board.GetPieceAt(move.From);

        // Remove piece from source
        hash ^= PieceKeys[(int)color, (int)piece, move.From];

        // Add piece to destination
        if (move.IsPromotion)
        {
            hash ^= PieceKeys[(int)color, (int)move.Promotion, move.To];
        }
        else
        {
            hash ^= PieceKeys[(int)color, (int)piece, move.To];
        }

        // Handle captures
        if (move.IsCapture)
        {
            if (move.IsEnPassant)
            {
                int captureSquare = move.To + (color == Color.White ? -8 : 8);
                hash ^= PieceKeys[1 - (int)color, (int)PieceType.Pawn, captureSquare];
            }
            else
            {
                var (capturedPiece, capturedColor) = board.GetPieceAt(move.To);
                if (capturedPiece != PieceType.None)
                {
                    hash ^= PieceKeys[(int)capturedColor, (int)capturedPiece, move.To];
                }
            }
        }

        // Handle castling
        if (move.IsCastling)
        {
            if (move.To > move.From) // Kingside
            {
                int rookFrom = move.To + 1;
                int rookTo = move.To - 1;
                hash ^= PieceKeys[(int)color, (int)PieceType.Rook, rookFrom];
                hash ^= PieceKeys[(int)color, (int)PieceType.Rook, rookTo];
            }
            else // Queenside
            {
                int rookFrom = move.To - 2;
                int rookTo = move.To + 1;
                hash ^= PieceKeys[(int)color, (int)PieceType.Rook, rookFrom];
                hash ^= PieceKeys[(int)color, (int)PieceType.Rook, rookTo];
            }
        }

        // Update castling rights (would need old castling rights to XOR properly)
        // This is a simplified version - in practice you'd track changes

        // Update en passant
        if (board.EnPassantSquare >= 0)
        {
            hash ^= EnPassantKeys[board.EnPassantSquare % 8];
        }
        if (move.IsDoublePush)
        {
            int epSquare = (move.From + move.To) / 2;
            hash ^= EnPassantKeys[epSquare % 8];
        }

        // Flip side to move
        hash ^= SideToMoveKey;

        return hash;
    }
}

// Update BoardExtensions to use proper Zobrist hashing
public static partial class BoardExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong GetZobristHash(this ref Board board)
    {
        return Zobrist.ComputeHash(ref board);
    }
}