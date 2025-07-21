namespace Chess;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Move : IEquatable<Move>
{
    private readonly uint _data;

    // Move encoding:
    // bits 0-5:   from square (0-63)
    // bits 6-11:  to square (0-63)
    // bits 12-13: promotion piece type (0=none, 1=knight, 2=bishop, 3=rook, 4=queen)
    // bit  14:    capture flag
    // bit  15:    double push flag
    // bit  16:    en passant flag
    // bit  17:    castling flag

    public const uint None = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Move(int from, int to, MoveFlags flags = MoveFlags.None, PieceType promotion = PieceType.None)
    {
        _data = (uint)from | (uint)to << 6 | (uint)promotion << 12 | (uint)flags;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Move(uint data)
    {
        _data = data;
    }

    public int From => (int)(_data & 0x3F);
    public int To => (int)(_data >> 6 & 0x3F);
    public PieceType Promotion => (PieceType)(_data >> 12 & 0x7);
    public bool IsCapture => (_data & (uint)MoveFlags.Capture) != 0;
    public bool IsDoublePush => (_data & (uint)MoveFlags.DoublePush) != 0;
    public bool IsEnPassant => (_data & (uint)MoveFlags.EnPassant) != 0;
    public bool IsCastling => (_data & (uint)MoveFlags.Castling) != 0;
    public bool IsPromotion => Promotion != PieceType.None;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Move CreateMove(int from, int to, MoveFlags flags = MoveFlags.None, PieceType promotion = PieceType.None)
    {
        return new Move(from, to, flags, promotion);
    }

    public bool Equals(Move other) => _data == other._data;
    public override bool Equals(object? obj) => obj is Move move && Equals(move);
    public override int GetHashCode() => (int)_data;
    public static bool operator ==(Move left, Move right) => left.Equals(right);
    public static bool operator !=(Move left, Move right) => !left.Equals(right);

    public override string ToString()
    {
        if (_data == None) return "none";

        string from = $"{(char)('a' + From % 8)}{From / 8 + 1}";
        string to = $"{(char)('a' + To % 8)}{To / 8 + 1}";
        string promotion = Promotion switch
        {
            PieceType.Queen => "q",
            PieceType.Rook => "r",
            PieceType.Bishop => "b",
            PieceType.Knight => "n",
            _ => ""
        };

        return from + to + promotion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Move(uint data) => new(data);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator uint(Move move) => move._data;
}

[Flags]
public enum MoveFlags : uint
{
    None = 0,
    Capture = 1 << 15,
    DoublePush = 1 << 16,
    EnPassant = 1 << 17,
    Castling = 1 << 18
}

public enum PieceType : byte
{
    None = 0,
    Knight = 1,
    Bishop = 2,
    Rook = 3,
    Queen = 4,
    King = 5,
    Pawn = 6
}

// Move list for stack allocation
[StructLayout(LayoutKind.Sequential)]
public struct MoveList
{
    private const int MaxMoves = 256;
    private int _count;
    private unsafe fixed uint _moves[MaxMoves];

    public readonly int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Add(Move move)
    {
        _moves[_count++] = move;
    }

    public unsafe readonly Move this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _moves[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _count = 0;
    }

    public readonly ReadOnlySpan<Move> AsSpan()
    {
        unsafe
        {
            fixed (uint* ptr = _moves)
            {
                return new ReadOnlySpan<Move>(ptr, _count);
            }
        }
    }
}