using System;
using System.Threading.Tasks;
using Move;
using Search;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Chess Engine Search Module");
        Console.WriteLine("==========================");

        // Initialize move generation tables
        Tables.Init();
        Zobrist.Init();

        if (args.Length > 0 && args[0] == "uci")
        {
            // Run in UCI mode
            var uci = new UCI();
            await uci.Run();
        }
        else
        {
            Console.WriteLine("UCI");
            // Demo mode
            var uci = new UCI();
            await uci.Run();
        }
    }

    static async Task RunDemo()
    {
        Console.WriteLine("\nRunning search demo...\n");

        // Create a position
        var position = new Position();
        Position.Set(Types.DEFAULT_FEN, position);

        // Create search engine
        var searchEngine = new Search.Search(128, 1); // 128MB hash, 4 threads

        // Set up search limits
        var limits = new SearchLimits
        {
            Depth = 10,        // Search to depth 10
            Time = 5000,       // 5 seconds max
            Infinite = false
        };

        Console.WriteLine("Starting position:");
        Console.WriteLine(position);

        Console.WriteLine("Searching...");
        var startTime = DateTime.Now;

        // Start search
        var result = searchEngine.StartSearch(position, limits);

        var elapsed = (DateTime.Now - startTime).TotalSeconds;

        // Display results
        Console.WriteLine($"\nSearch completed in {elapsed:F2} seconds");
        Console.WriteLine($"Best move: {result.BestMove}");
        Console.WriteLine($"Score: {result.Score} cp");
        Console.WriteLine($"Depth reached: {result.Depth}");
        Console.WriteLine($"Nodes searched: {result.Nodes:N0}");
        Console.WriteLine($"Nodes per second: {(int)(result.Nodes / elapsed):N0}");

        if (result.Pv.Length > 0)
        {
            Console.WriteLine($"Principal variation: {string.Join(" ", result.Pv)}");
        }

        // Test different positions
        Console.WriteLine("\n\nTesting tactical position (Kiwipete):");
        Position.Set(Types.KIWIPETE, position);
        Console.WriteLine(position);

        Console.WriteLine("Searching...");
        startTime = DateTime.Now;

        result = searchEngine.StartSearch(position, limits);

        elapsed = (DateTime.Now - startTime).TotalSeconds;

        Console.WriteLine($"\nSearch completed in {elapsed:F2} seconds");
        Console.WriteLine($"Best move: {result.BestMove}");
        Console.WriteLine($"Score: {result.Score} cp");
        Console.WriteLine($"Depth reached: {result.Depth}");
        Console.WriteLine($"Nodes searched: {result.Nodes:N0}");
        Console.WriteLine($"Nodes per second: {(int)(result.Nodes / elapsed):N0}");

        // Perft test
        Console.WriteLine("\n\nRunning perft test (depth 5):");
        var perftResult = RunPerft(position, 5);
        Console.WriteLine($"Perft(5) = {perftResult:N0} nodes");
    }

    static ulong RunPerft(Position pos, int depth)
    {
        if (depth == 0)
            return 1;

        ulong nodes = 0;
        var moves = pos.Turn == Color.White ?
            pos.GenerateLegals<White>() :
            pos.GenerateLegals<Black>();

        if (depth == 1)
            return (ulong)moves.Length;

        foreach (var move in moves)
        {
            pos.Play(pos.Turn, move);
            nodes += RunPerft(pos, depth - 1);
            pos.Undo(pos.Turn.Flip(), move);
        }

        return nodes;
    }
}