using System;
using System.Threading.Tasks;
using Move;
using Search;

class Program
{
    static async Task Main(string[] args)
    {
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
        var searchEngine = new Search.Search(128); // 128MB hash, 4 threads

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
    }

    
}