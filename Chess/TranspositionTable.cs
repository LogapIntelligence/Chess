namespace Chess;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TTEntry
{
    public ulong Hash;
    public short Score;
    public Move Move;
    public byte Depth;
    public TTFlag Flag;
}

public enum TTFlag : byte
{
    None = 0,
    Exact = 1,
    LowerBound = 2,
    UpperBound = 3
}

public class TranspositionTable
{
    private readonly TTEntry[] _entries;
    private readonly int _size;

    public TranspositionTable(int sizeMb)
    {
        int entrySize = Marshal.SizeOf<TTEntry>();
        _size = (sizeMb * 1024 * 1024) / entrySize;
        _entries = new TTEntry[_size];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TTEntry Probe(ulong hash)
    {
        int index = (int)(hash % (ulong)_size);
        return _entries[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Store(ulong hash, int depth, int score, TTFlag flag, Move move)
    {
        int index = (int)(hash % (ulong)_size);
        ref TTEntry entry = ref _entries[index];

        // Always replace scheme - simple but effective
        // More sophisticated schemes would check depth and age
        entry.Hash = hash;
        entry.Score = (short)Math.Clamp(score, short.MinValue, short.MaxValue);
        entry.Depth = (byte)Math.Min(depth, 255);
        entry.Flag = flag;
        entry.Move = move;
    }

    public void Clear()
    {
        Array.Clear(_entries, 0, _entries.Length);
    }

    public int Usage()
    {
        // Sample first 1000 entries to estimate usage
        int used = 0;
        int sample = Math.Min(1000, _size);

        for (int i = 0; i < sample; i++)
        {
            if (_entries[i].Hash != 0)
                used++;
        }

        return (used * 1000) / sample; // Per mille
    }
}