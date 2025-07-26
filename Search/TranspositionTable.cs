using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Move;

namespace Search
{
    public enum TTFlag : byte
    {
        None = 0,
        Exact = 1,
        LowerBound = 2,
        UpperBound = 3
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TTEntry
    {
        public ulong Key;
        public Move.Move Move;
        public short Score;
        public byte Depth;
        public TTFlag Flag;
        public byte Age;

        public TTEntry(ulong key, Move.Move move, int score, int depth, TTFlag flag, int age)
        {
            Key = key;
            Move = move;
            Score = (short)score;
            Depth = (byte)depth;
            Flag = flag;
            Age = (byte)age;
        }
    }

    public class TranspositionTable
    {
        private readonly TTEntry[] entries;
        private readonly int mask;
        private int currentAge;

        public TranspositionTable(int sizeMB)
        {
            // Calculate number of entries
            var entrySize = Marshal.SizeOf<TTEntry>();
            var numEntries = (sizeMB * 1024 * 1024) / entrySize;

            // Round down to power of 2
            numEntries = 1 << (31 - System.Numerics.BitOperations.LeadingZeroCount((uint)numEntries));

            entries = new TTEntry[numEntries];
            mask = numEntries - 1;
            currentAge = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(ulong key, int depth, int score, TTFlag flag, Move.Move move)
        {
            var index = (int)(key & (ulong)mask);
            ref var entry = ref entries[index];

            // Replace if empty, same position, or better depth/newer
            if (entry.Key == 0 ||
                (entry.Key ^ (ulong)entry.Move.ToFrom) == key ||
                entry.Age != currentAge ||
                depth >= entry.Depth)
            {
                entry = new TTEntry(key ^ (ulong)move.ToFrom, move, score, depth, flag, currentAge);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TTEntry? Probe(ulong key)
        {
            var index = (int)(key & (ulong)mask);
            ref var entry = ref entries[index];

            if (entry.Key != 0 && (entry.Key ^ (ulong)entry.Move.ToFrom) == key)
            {
                entry.Age = (byte)currentAge; // Refresh age
                return entry;
            }

            return null;
        }

        public void Clear()
        {
            Array.Clear(entries, 0, entries.Length);
        }

        public void NewSearch()
        {
            currentAge = (currentAge + 1) & 255;
        }

        public int HashFull()
        {
            int used = 0;
            int sampleSize = Math.Min(1000, entries.Length);

            for (int i = 0; i < sampleSize; i++)
            {
                if (entries[i].Age == currentAge && entries[i].Key != 0)
                    used++;
            }

            return used * 1000 / sampleSize;
        }
    }
}