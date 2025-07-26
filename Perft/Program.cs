using Move;
using Perft;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;


static unsafe void RunOptimizedPerft()
{
    var p = new Position();
    Position.Set("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -", p);
    Console.WriteLine(p);

    // Warmup
    for (int i = 0; i < 10; i++)
    {
        _ = PerftOptimized.Run(p, 4);
    }

    Console.WriteLine("=== Optimized Perft Results ===\n");

    for (uint depth = 1; depth <= 7; depth++)
    {
        var sw = Stopwatch.StartNew();
        var nodes = PerftOptimized.Run(p, depth);
        sw.Stop();

        var nps = (nodes * 1000000.0 / sw.ElapsedTicks * Stopwatch.Frequency / 1000000);
        Console.WriteLine($"Perft({depth}): {nodes,15:N0} nodes | " +
                        $"{sw.ElapsedMilliseconds,7:N0} ms | " +
                        $"{nps/1_000_000:F2} Mnps");
    }
}

// Keep original implementation for comparison
static ulong PerftGeneric(Position p, Color color, uint depth)
{
    if (color == Color.White)
        return Perft<White>(p, depth);
    else
        return Perft<Black>(p, depth);
}

static ulong Perft<TColor>(Position p, uint depth) where TColor : IColor, new()
{
    ulong nodes = 0;
    var us = new TColor();
    var them = us.Opposite();
    var list = new MoveList<TColor>(p);

    if (depth == 1) return (ulong)list.Count;

    foreach (var move in list)
    {
        p.Play(us.Value, move);
        nodes += PerftGeneric(p, them.Value, depth - 1);
        p.Undo(us.Value, move);
    }

    return nodes;
}


// Initialize tables
Tables.InitialiseAllDatabases();
Zobrist.InitialiseZobristKeys();
Console.WriteLine("Chess Move Generator - Perft Benchmark\n");

// Allow unsafe code
RunOptimizedPerft();

Console.WriteLine("\n" + new string('=', 60) + "\n");



// release nps log
// ~155 mnps
// ~169 mnps
// ~174 mnps
// ~300 mnps