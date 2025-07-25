namespace Chess;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
public struct TTEntry
{
    public uint HashHigh;    // 4 bytes - upper 32 bits of hash
    public short Score;      // 2 bytes
    public ushort MoveData;  // 2 bytes - compressed move
    public byte Depth;       // 1 byte
    public byte Flags;       // 1 byte - includes bound type and age
    public byte Eval;        // 1 byte - static eval / 8
    private byte _padding1;  // 1 byte padding
    private uint _padding2;  // 4 bytes padding to reach 16 bytes

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool MatchesHash(ulong fullHash)
    {
        return HashHigh == (uint)(fullHash >> 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly TTFlag GetFlag()
    {
        return (TTFlag)(Flags & 0x03);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int GetAge()
    {
        return Flags >> 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetFlag(TTFlag flag, int age)
    {
        Flags = (byte)((age << 2) | (int)flag);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Move GetMove()
    {
        if (MoveData == 0) return default;

        int from = MoveData & 0x3F;
        int to = (MoveData >> 6) & 0x3F;
        int flags = MoveData >> 12;

        return new Move(from, to, (MoveFlags)flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetMove(Move move)
    {
        if (move == default)
        {
            MoveData = 0;
        }
        else
        {
            MoveData = (ushort)(move.From | (move.To << 6) | ((int)move.GetFlags() << 12));
        }
    }
}

public enum TTFlag : byte
{
    None = 0,
    Exact = 1,
    LowerBound = 2,
    UpperBound = 3
}

public unsafe class TranspositionTable
{
    private const int ClusterSize = 3; // 3 entries per cluster for better replacement
    private const int EntrySize = 16; // TTEntry is 16 bytes
    private const int CacheLineSize = 64; // Typical cache line size
    private const int EntriesPerCacheLine = CacheLineSize / EntrySize; // 4 entries per cache line

    private readonly TTEntry* _entries;
    private readonly int _clusterCount;
    private readonly ulong _indexMask;
    private readonly GCHandle _handle;
    private readonly byte[] _memory;
    private int _currentAge;

    public TranspositionTable(int sizeMb)
    {
        // Ensure size is power of 2 for fast indexing
        int totalBytes = sizeMb * 1024 * 1024;
        int entryCount = totalBytes / EntrySize;

        // Round down to nearest power of 2
        int powerOfTwo = 1;
        while (powerOfTwo * 2 <= entryCount)
            powerOfTwo *= 2;

        entryCount = powerOfTwo;
        _clusterCount = entryCount / ClusterSize;
        _indexMask = (ulong)(_clusterCount - 1);

        // Allocate aligned memory
        _memory = GC.AllocateArray<byte>(entryCount * EntrySize, pinned: true);
        _handle = GCHandle.Alloc(_memory, GCHandleType.Pinned);
        _entries = (TTEntry*)_handle.AddrOfPinnedObject();

        Clear();
    }

    ~TranspositionTable()
    {
        if (_handle.IsAllocated)
            _handle.Free();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TTEntry Probe(ulong hash)
    {
        ulong index = (hash & _indexMask) * ClusterSize;
        TTEntry* cluster = _entries + index;

        // Check all entries in cluster
        for (int i = 0; i < ClusterSize; i++)
        {
            if (cluster[i].MatchesHash(hash))
            {
                // Refresh age on hit
                cluster[i].Flags = (byte)((_currentAge << 2) | (cluster[i].Flags & 0x03));
                return cluster[i];
            }
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Store(ulong hash, int depth, int score, TTFlag flag, Move move)
    {
        ulong index = (hash & _indexMask) * ClusterSize;
        TTEntry* cluster = _entries + index;

        // Find entry to replace
        int replaceIndex = 0;
        int minScore = int.MaxValue;

        for (int i = 0; i < ClusterSize; i++)
        {
            // Always replace exact hash match
            if (cluster[i].MatchesHash(hash))
            {
                replaceIndex = i;
                break;
            }

            // Calculate replacement score (lower is more replaceable)
            int entryAge = cluster[i].GetAge();
            int ageBonus = (_currentAge - entryAge) * 256;
            int depthMalus = cluster[i].Depth * 16;
            int replaceScore = depthMalus - ageBonus;

            if (replaceScore < minScore)
            {
                minScore = replaceScore;
                replaceIndex = i;
            }
        }

        // Store entry
        ref TTEntry entry = ref cluster[replaceIndex];
        entry.HashHigh = (uint)(hash >> 32);
        entry.Score = (short)Math.Clamp(score, short.MinValue, short.MaxValue);
        entry.Depth = (byte)Math.Min(depth, 255);
        entry.SetFlag(flag, _currentAge);
        entry.SetMove(move);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Prefetch(ulong hash)
    {
        // Prefetch cache line containing the cluster
        ulong index = (hash & _indexMask) * ClusterSize;
        TTEntry* cluster = _entries + index;

        if (RuntimeInformation.ProcessArchitecture == Architecture.X64 ||
            RuntimeInformation.ProcessArchitecture == Architecture.X86)
        {
            // x86/x64 prefetch instruction via intrinsics
            System.Runtime.Intrinsics.X86.Sse.Prefetch0(cluster);
        }
    }

    public void Clear()
    {
        new Span<byte>(_memory).Clear();
        _currentAge = 0;
    }

    public void NewSearch()
    {
        _currentAge = (_currentAge + 1) & 0x3F; // 6-bit age
    }

    public int Usage()
    {
        const int SampleSize = 1000;
        int used = 0;
        int samplesToCheck = Math.Min(SampleSize, _clusterCount);

        for (int i = 0; i < samplesToCheck; i++)
        {
            int clusterIndex = i * _clusterCount / samplesToCheck;
            TTEntry* cluster = _entries + (clusterIndex * ClusterSize);

            for (int j = 0; j < ClusterSize; j++)
            {
                if (cluster[j].HashHigh != 0)
                    used++;
            }
        }

        return (used * 1000) / (samplesToCheck * ClusterSize);
    }
}

// Extension to make Move work with compressed format
public static class MoveExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MoveFlags GetFlags(this Move move)
    {
        MoveFlags flags = MoveFlags.None;

        if (move.IsCapture) flags |= MoveFlags.Capture;
        if (move.IsDoublePush) flags |= MoveFlags.DoublePush;
        if (move.IsEnPassant) flags |= MoveFlags.EnPassant;
        if (move.IsCastling) flags |= MoveFlags.Castling;

        // Encode promotion in upper bits
        if (move.IsPromotion)
        {
            flags |= (MoveFlags)((int)move.Promotion << 19);
        }

        return flags;
    }
}