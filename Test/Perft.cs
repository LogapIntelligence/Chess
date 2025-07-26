using Move;
using System;
using System.Diagnostics;
namespace Test;
public static class Perft
{
    public static unsafe void Run()
    {
        var p = new Position();
        Position.Set("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -", p);

        // Expected perft values
        var expectedValues = new[]
        {
            (1U, 20UL),
            (2U, 400UL),
            (3U, 8_902UL),
            (4U, 197_281UL),
            (5U, 4_865_609UL),
            (6U, 119_060_324UL),
            (7U, 3_195_901_860UL),
            (8U, 84_998_978_956UL)
        };

        // Warmup
        for (int i = 0; i < 10; i++)
            _ = PerftOptimized.Run(p, 4);

        // Test each depth up to 6
        ulong nodes = 0;
        var sw = new Stopwatch();

        foreach (var (depth, expected) in expectedValues)
        {
            if (depth > 6) break;

            if (depth == 6)
            {
                sw.Start();
                nodes = PerftOptimized.Run(p, depth);
                sw.Stop();
            }
            else
            {
                nodes = PerftOptimized.Run(p, depth);
            }

            var passed = nodes == expected;
            Console.WriteLine($"[{(passed ? "Passed" : "Failed")}] Perft {depth}");
        }

        var nps = nodes * 1000000.0 / sw.ElapsedTicks * Stopwatch.Frequency / 1000000;
        Console.WriteLine($"[NPS] {nps:F0}");
    }

    static ulong Pg(Position p, Color color, uint depth)
        => color == Color.White ? P<White>(p, depth) : P<Black>(p, depth);

    static ulong P<TColor>(Position p, uint depth) where TColor : IColor, new()
    {
        var us = new TColor();
        var list = new MoveList<TColor>(p);
        if (depth == 1) return (ulong)list.Count;
        ulong nodes = 0;
        foreach (var move in list)
        {
            p.Play(us.Value, move);
            nodes += Pg(p, us.Opposite().Value, depth - 1);
            p.Undo(us.Value, move);
        }
        return nodes;
    }
}