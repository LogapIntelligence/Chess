using Move;
using System.ComponentModel;
using System.Diagnostics;

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

static ulong PerftGeneric(Position p, Color color, uint depth)
{
    if (color == Color.White)
        return Perft<White>(p, depth);
    else
        return Perft<Black>(p, depth);
}

static void PerftDiv<TColor>(Position p, uint depth) where TColor : IColor, new()
{
    ulong nodes = 0, pf;
    var us = new TColor();
    var them = us.Opposite();

    var list = new MoveList<TColor>(p);

    foreach (var move in list)
    {
        Console.Write(move);

        p.Play(us.Value, move);
        pf = PerftGeneric(p, them.Value, depth - 1);
        Console.WriteLine($": {pf} moves");
        nodes += pf;
        p.Undo(us.Value, move);
    }

    Console.WriteLine($"\nTotal: {nodes} moves");
}

static void TestPerft()
{
    var p = new Position();
    Position.Set("rnbqkbnr/pppppppp/8/8/8/8/PPPP1PPP/RNBQKBNR w KQkq -", p);
    Console.WriteLine(p);

    var sw = Stopwatch.StartNew();
    var n = Perft<White>(p, 6);
    sw.Stop();

    Console.WriteLine($"Nodes: {n}");
    Console.WriteLine($"NPS: {(int)(n * 1000000.0 / sw.ElapsedTicks * Stopwatch.Frequency / 1000000)}");
    Console.WriteLine($"Time difference = {sw.ElapsedMilliseconds} [milliseconds]");
}

static void Main(string[] args)
{
    Tables.InitialiseAllDatabases();
    Zobrist.InitialiseZobristKeys();

    TestPerft();
}