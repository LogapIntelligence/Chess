using Move;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Perft
{
    public static class PerftProfiler
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ProfileMoveGeneration()
        {
            var pos = new Position();
            Position.Set(Types.DEFAULT_FEN, pos);

            const int iterations = 1_000_000;
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                if (pos.Turn == Color.White)
                    _ = new MoveList<White>(pos);
                else
                    _ = new MoveList<Black>(pos);
            }

            sw.Stop();
            var timePerGen = sw.Elapsed.TotalNanoseconds / iterations;
            Console.WriteLine($"Move generation: {timePerGen:F1} ns per position");
            Console.WriteLine($"Throughput: {1_000_000_000 / timePerGen:F0} positions/second");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ProfileMakeUnmake()
        {
            var pos = new Position();
            Position.Set(Types.DEFAULT_FEN, pos);
            var moves = new MoveList<White>(pos);
            var move = moves.First();

            const int iterations = 10_000_000;
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                pos.Play(Color.White, move);
                pos.Undo(Color.White, move);
            }

            sw.Stop();
            var timePerOp = sw.Elapsed.TotalNanoseconds / (iterations * 2);
            Console.WriteLine($"Make/Unmake: {timePerOp:F1} ns per operation");
        }
    }
}
