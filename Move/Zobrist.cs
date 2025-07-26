using System;

namespace Move
{
    public class PRNG
    {
        private ulong s;

        public PRNG(ulong seed)
        {
            s = seed;
        }
        private ulong Rand64()
        {
            s ^= s >> 12;
            s ^= s << 25;
            s ^= s >> 27;
            return s * 2685821657736338717UL;
        }

        public T Rand<T>() where T : struct
        {
            if (typeof(T) == typeof(ulong))
                return (T)(object)Rand64();
            throw new NotSupportedException($"Type {typeof(T)} is not supported");
        }

        public T SparseRand<T>() where T : struct
        {
            if (typeof(T) == typeof(ulong))
                return (T)(object)(Rand64() & Rand64() & Rand64());
            throw new NotSupportedException($"Type {typeof(T)} is not supported");
        }
    }

    public static class Zobrist
    {
        public static readonly ulong[,] ZobristTable = new ulong[Types.NPIECES, Types.NSQUARES];
        public static ulong SideToMove;
        public static void Init()
        {
            PRNG rng = new(70026072);

            SideToMove = rng.Rand<ulong>();

            for (int i = 0; i < Types.NPIECES; i++)
            {
                for (int j = 0; j < Types.NSQUARES; j++)
                {
                    ZobristTable[i, j] = rng.Rand<ulong>();
                }
            }
        }
    }
}