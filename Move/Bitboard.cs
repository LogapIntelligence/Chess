using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Move
{
    public static class Bitboard
    {
        public static readonly ulong[] MASK_FILE = new ulong[]
        {
            0x0101010101010101UL, 0x0202020202020202UL, 0x0404040404040404UL, 0x0808080808080808UL,
            0x1010101010101010UL, 0x2020202020202020UL, 0x4040404040404040UL, 0x8080808080808080UL
        };

        public static readonly ulong[] MASK_RANK = new ulong[]
        {
            0xffUL, 0xff00UL, 0xff0000UL, 0xff000000UL,
            0xff00000000UL, 0xff0000000000UL, 0xff000000000000UL, 0xff00000000000000UL
        };

        public static readonly ulong[] MASK_DIAGONAL = new ulong[]
        {
            0x80UL, 0x8040UL, 0x804020UL,
            0x80402010UL, 0x8040201008UL, 0x804020100804UL,
            0x80402010080402UL, 0x8040201008040201UL, 0x4020100804020100UL,
            0x2010080402010000UL, 0x1008040201000000UL, 0x804020100000000UL,
            0x402010000000000UL, 0x201000000000000UL, 0x100000000000000UL
        };

        public static readonly ulong[] MASK_ANTI_DIAGONAL = new ulong[]
        {
            0x1UL, 0x102UL, 0x10204UL,
            0x1020408UL, 0x102040810UL, 0x10204081020UL,
            0x1020408102040UL, 0x102040810204080UL, 0x204081020408000UL,
            0x408102040800000UL, 0x810204080000000UL, 0x1020408000000000UL,
            0x2040800000000000UL, 0x4080000000000000UL, 0x8000000000000000UL
        };

        public static readonly ulong[] SQUARE_BB = new ulong[]
        {
            0x1UL, 0x2UL, 0x4UL, 0x8UL,
            0x10UL, 0x20UL, 0x40UL, 0x80UL,
            0x100UL, 0x200UL, 0x400UL, 0x800UL,
            0x1000UL, 0x2000UL, 0x4000UL, 0x8000UL,
            0x10000UL, 0x20000UL, 0x40000UL, 0x80000UL,
            0x100000UL, 0x200000UL, 0x400000UL, 0x800000UL,
            0x1000000UL, 0x2000000UL, 0x4000000UL, 0x8000000UL,
            0x10000000UL, 0x20000000UL, 0x40000000UL, 0x80000000UL,
            0x100000000UL, 0x200000000UL, 0x400000000UL, 0x800000000UL,
            0x1000000000UL, 0x2000000000UL, 0x4000000000UL, 0x8000000000UL,
            0x10000000000UL, 0x20000000000UL, 0x40000000000UL, 0x80000000000UL,
            0x100000000000UL, 0x200000000000UL, 0x400000000000UL, 0x800000000000UL,
            0x1000000000000UL, 0x2000000000000UL, 0x4000000000000UL, 0x8000000000000UL,
            0x10000000000000UL, 0x20000000000000UL, 0x40000000000000UL, 0x80000000000000UL,
            0x100000000000000UL, 0x200000000000000UL, 0x400000000000000UL, 0x800000000000000UL,
            0x1000000000000000UL, 0x2000000000000000UL, 0x4000000000000000UL, 0x8000000000000000UL,
            0x0UL
        };

        public const ulong WHITE_OO_MASK = 0x90UL;
        public const ulong WHITE_OOO_MASK = 0x11UL;
        public const ulong WHITE_OO_BLOCKERS_AND_ATTACKERS_MASK = 0x60UL;
        public const ulong WHITE_OOO_BLOCKERS_AND_ATTACKERS_MASK = 0xeUL;
        public const ulong BLACK_OO_MASK = 0x9000000000000000UL;
        public const ulong BLACK_OOO_MASK = 0x1100000000000000UL;
        public const ulong BLACK_OO_BLOCKERS_AND_ATTACKERS_MASK = 0x6000000000000000UL;
        public const ulong BLACK_OOO_BLOCKERS_AND_ATTACKERS_MASK = 0xE00000000000000UL;
        public const ulong ALL_CASTLING_MASK = 0x9100000000000091UL;

        private static readonly int[] DEBRUIJN64 = new int[]
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

        private const ulong MAGIC = 0x03f79d71b4cb0a89UL;

        public static void PrintBitboard(ulong b)
        {
            for (int i = 56; i >= 0; i -= 8)
            {
                for (int j = 0; j < 8; j++)
                    Console.Write((char)(((b >> (i + j)) & 1) + '0') + " ");
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        public static int PopCount(ulong x)
        {
            const ulong k1 = 0x5555555555555555UL;
            const ulong k2 = 0x3333333333333333UL;
            const ulong k4 = 0x0f0f0f0f0f0f0f0fUL;
            const ulong kf = 0x0101010101010101UL;

            x = x - ((x >> 1) & k1);
            x = (x & k2) + ((x >> 2) & k2);
            x = (x + (x >> 4)) & k4;
            x = (x * kf) >> 56;
            return (int)x;
        }

        public static int SparsePopCount(ulong x)
        {
            int count = 0;
            while (x != 0)
            {
                count++;
                x &= x - 1;
            }
            return count;
        }

        public static Square PopLsb(ref ulong b)
        {
            Square lsb = Bsf(b);  // Now the types match
            b &= b - 1;
            return lsb;  // No cast needed
        }

        public static Square Bsf(ulong b)
        {
            return (Square)DEBRUIJN64[MAGIC * (b ^ (b - 1)) >> 58];
        }

        public static ulong Shift(Direction d, ulong b)
        {
            switch (d)
            {
                case Direction.North: return b << 8;
                case Direction.South: return b >> 8;
                case Direction.NorthNorth: return b << 16;
                case Direction.SouthSouth: return b >> 16;
                case Direction.East: return (b & ~MASK_FILE[(int)File.FileH]) << 1;
                case Direction.West: return (b & ~MASK_FILE[(int)File.FileA]) >> 1;
                case Direction.NorthEast: return (b & ~MASK_FILE[(int)File.FileH]) << 9;
                case Direction.NorthWest: return (b & ~MASK_FILE[(int)File.FileA]) << 7;
                case Direction.SouthEast: return (b & ~MASK_FILE[(int)File.FileH]) >> 7;
                case Direction.SouthWest: return (b & ~MASK_FILE[(int)File.FileA]) >> 9;
                default: return 0;
            }
        }

        public static ulong OoMask(Color c) => c == Color.White ? WHITE_OO_MASK : BLACK_OO_MASK;
        public static ulong OooMask(Color c) => c == Color.White ? WHITE_OOO_MASK : BLACK_OOO_MASK;
        public static ulong OoBlockersMask(Color c) =>
            c == Color.White ? WHITE_OO_BLOCKERS_AND_ATTACKERS_MASK : BLACK_OO_BLOCKERS_AND_ATTACKERS_MASK;
        public static ulong OooBlockersMask(Color c) =>
            c == Color.White ? WHITE_OOO_BLOCKERS_AND_ATTACKERS_MASK : BLACK_OOO_BLOCKERS_AND_ATTACKERS_MASK;
        public static ulong IgnoreOooDanger(Color c) => c == Color.White ? 0x2UL : 0x200000000000000UL;
    }
}
