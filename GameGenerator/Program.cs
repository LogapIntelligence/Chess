using System;
using System.IO;

namespace GameGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            int amount;
            int depth;

            // --- Get Amount from User ---
            while (true)
            {
                Console.Write("Enter amount: ");
                string input = Console.ReadLine();
                if (int.TryParse(input, out amount) && amount > 0)
                {
                    break; // Exit loop if input is a valid positive integer
                }
                Console.WriteLine("Error: Amount must be a positive integer. Please try again.");
            }

            // --- Get Depth from User ---
            while (true)
            {
                Console.Write("Enter depth: ");
                string input = Console.ReadLine();
                if (int.TryParse(input, out depth) && depth > 0)
                {
                    break; // Exit loop if input is a valid positive integer
                }
                Console.WriteLine("Error: Depth must be a positive integer. Please try again.");
            }

            // --- Generate Games ---
            try
            {
                Console.WriteLine($"\nGenerating {amount} games with depth {depth}...");
                GenerateGames(amount, depth);
                Console.WriteLine("Games generated successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating games: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void GenerateGames(int amount, int depth)
        {
            Console.WriteLine($"Generating {amount} games with depth {depth}...");

            // Create Games directory if it doesn't exist
            string gamesDir = Path.Combine(Directory.GetCurrentDirectory(), "Games");
            Directory.CreateDirectory(gamesDir);

            // Initialize Stockfish
            var stockfish = new StockfishHelper();
            if (!stockfish.Initialize())
            {
                Console.WriteLine("Error: Failed to initialize Stockfish. Make sure stockfish is in PATH or current directory.");
                return;
            }

            var generator = new GameGenerator(stockfish, depth);
            int gamesGenerated = 0;
            int failures = 0;

            var startTime = DateTime.Now;

            for (int i = 0; i < amount; i++)
            {
                try
                {
                    string gameId = Guid.NewGuid().ToString();
                    string filePath = Path.Combine(gamesDir, gameId);

                    Console.Write($"\rGenerating game {i + 1}/{amount}... ");

                    var game = generator.GenerateGame();
                    GameWriter.WriteGame(filePath, game);
                    gamesGenerated++;

                    // Print progress
                    if ((i + 1) % 10 == 0)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var rate = gamesGenerated / elapsed.TotalSeconds;
                        Console.Write($"({rate:F1} games/sec)");
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"\nWarning: Failed to generate game {i + 1}: {ex.Message}");
                }
            }

            stockfish.Dispose();

            var totalTime = DateTime.Now - startTime;
            Console.WriteLine($"\n\nGeneration complete!");
            Console.WriteLine($"Games generated: {gamesGenerated}");
            Console.WriteLine($"Failures: {failures}");
            Console.WriteLine($"Total time: {totalTime.TotalSeconds:F1} seconds");
            Console.WriteLine($"Average time per game: {totalTime.TotalSeconds / gamesGenerated:F2} seconds");
            Console.WriteLine($"Games saved to: {gamesDir}");
        }
    }
}