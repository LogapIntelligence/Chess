using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace Chess
{
    public static class Performance
    {
        private const int WarmupIterations = 1000;

        public static void RunAllBenchmarks()
        {
            Console.WriteLine("=== CHESS ENGINE PERFORMANCE BENCHMARKS ===");
            Console.WriteLine($"Environment: {Environment.ProcessorCount} cores, {Environment.OSVersion}");
            Console.WriteLine($"GC Mode: {(GCSettings.IsServerGC ? "Server" : "Workstation")}, Latency: {GCSettings.LatencyMode}");
            Console.WriteLine();

            // Force GC before benchmarks
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            BenchmarkMoveGeneration();
            BenchmarkPerft();
            BenchmarkMakeUnmake();
            BenchmarkAttackDetection();
            BenchmarkMagicBitboards();
            BenchmarkMemoryPressure();
            BenchmarkDifferentPositions();

            Console.WriteLine("\n=== BENCHMARK COMPLETE ===");
        }

        private static void BenchmarkMoveGeneration()
        {
            Console.WriteLine("Move Generation Benchmark:");
            Console.WriteLine("-------------------------");

            var positions = new[]
            {
            ("Starting position", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"),
            ("Middlegame", "r1bq1rk1/pp2nppp/2n1p3/3p4/1b1P4/2NBP3/PP2NPPP/R1BQKR2 w Q - 0 1"),
            ("Endgame", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1"),
            ("Complex", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1"),
            ("Max mobility", "R6R/3Q4/1Q4Q1/4Q3/2Q4Q/Q4Q2/pp1Q4/kBNN1KB1 w - - 0 1")
        };

            foreach (var (name, fen) in positions)
            {
                var board = FenParser.ParseFen(fen);
                var moves = new MoveList();

                // Warmup
                for (int i = 0; i < WarmupIterations; i++)
                {
                    moves.Clear();
                    MoveGenerator.GenerateMoves(ref board, ref moves);
                }

                // Benchmark
                const int iterations = 1_000_000;
                var sw = Stopwatch.StartNew();

                for (int i = 0; i < iterations; i++)
                {
                    moves.Clear();
                    MoveGenerator.GenerateMoves(ref board, ref moves);
                }

                sw.Stop();

                double posPerSec = iterations / sw.Elapsed.TotalSeconds;
                double nsPerPos = sw.Elapsed.TotalNanoseconds / iterations;

                Console.WriteLine($"{name,-20} {posPerSec,12:N0} pos/sec  {nsPerPos,8:N0} ns/pos  ({moves.Count} moves)");
            }
            Console.WriteLine();
        }

        private static void BenchmarkPerft()
        {
            Console.WriteLine("Perft Performance Benchmark:");
            Console.WriteLine("---------------------------");

            var board = Board.StartingPosition();

            for (int depth = 1; depth <= 7; depth++)
            {
                // Skip depth 7 if it would take too long
                if (depth == 7)
                {
                    var testRun = Stopwatch.StartNew();
                    Perft.RunPerft(board, 5);
                    testRun.Stop();

                    if (testRun.ElapsedMilliseconds > 100)
                    {
                        Console.WriteLine($"Perft({depth}): Skipped (would take ~{testRun.ElapsedMilliseconds * 120 / 1000}s)");
                        continue;
                    }
                }

                var sw = Stopwatch.StartNew();
                long nodes = Perft.RunPerft(board, depth);
                sw.Stop();

                double nps = nodes / sw.Elapsed.TotalSeconds;
                Console.WriteLine($"Perft({depth}): {nodes,15:N0} nodes in {sw.ElapsedMilliseconds,6}ms = {nps,12:N0} NPS");
            }
            Console.WriteLine();
        }

        private static void BenchmarkMakeUnmake()
        {
            Console.WriteLine("Make/Unmake Move Benchmark:");
            Console.WriteLine("--------------------------");

            var board = Board.StartingPosition();
            var moves = new MoveList();
            MoveGenerator.GenerateMoves(ref board, ref moves);

            // Test different move types
            var testMoves = new[]
            {
            ("Quiet move", new Move(12, 28)),
            ("Capture", new Move(12, 28, MoveFlags.Capture)),
            ("Castle", new Move(4, 6, MoveFlags.Castling)),
            ("En passant", new Move(35, 42, MoveFlags.EnPassant | MoveFlags.Capture)),
            ("Promotion", new Move(48, 56, MoveFlags.None, PieceType.Queen))
        };

            const int iterations = 10_000_000;

            foreach (var (name, move) in testMoves)
            {
                var sw = Stopwatch.StartNew();

                for (int i = 0; i < iterations; i++)
                {
                    var copy = board;
                    copy.MakeMove(move);
                }

                sw.Stop();

                double movesPerSec = iterations / sw.Elapsed.TotalSeconds;
                double nsPerMove = sw.Elapsed.TotalNanoseconds / iterations;

                Console.WriteLine($"{name,-15} {movesPerSec,12:N0} moves/sec  {nsPerMove,6:N0} ns/move");
            }
            Console.WriteLine();
        }

        private static void BenchmarkAttackDetection()
        {
            Console.WriteLine("Attack Detection Benchmark:");
            Console.WriteLine("--------------------------");

            var positions = new[]
            {
            ("Empty board", "8/8/8/3k4/8/3K4/8/8 w - - 0 1"),
            ("Starting pos", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"),
            ("Complex", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")
        };

            const int iterations = 10_000_000;

            foreach (var (name, fen) in positions)
            {
                var board = FenParser.ParseFen(fen);

                // Test various squares
                int[] testSquares = { 0, 27, 36, 60 }; // corners and center

                var sw = Stopwatch.StartNew();

                for (int i = 0; i < iterations; i++)
                {
                    foreach (int sq in testSquares)
                    {
                        board.IsSquareAttacked(sq, Color.White);
                        board.IsSquareAttacked(sq, Color.Black);
                    }
                }

                sw.Stop();

                double checksPerSec = (iterations * testSquares.Length * 2) / sw.Elapsed.TotalSeconds;
                double nsPerCheck = sw.Elapsed.TotalNanoseconds / (iterations * testSquares.Length * 2);

                Console.WriteLine($"{name,-15} {checksPerSec,12:N0} checks/sec  {nsPerCheck,6:N0} ns/check");
            }
            Console.WriteLine();
        }

        private static void BenchmarkMagicBitboards()
        {
            Console.WriteLine("Magic Bitboards Benchmark:");
            Console.WriteLine("-------------------------");

            const int iterations = 100_000_000;
            ulong[] occupancies = { 0UL, 0x00FF00FF00FF00FFUL, 0xFFFFFFFFFFFFFFFFUL, 0x0042000000004200UL };

            // Benchmark bishop attacks
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                foreach (var occ in occupancies)
                {
                    MagicBitboards.GetBishopAttacks(27, occ); // d4
                    MagicBitboards.GetBishopAttacks(0, occ);  // a1
                    MagicBitboards.GetBishopAttacks(63, occ); // h8
                }
            }
            sw.Stop();

            double bishopPerSec = (iterations * occupancies.Length * 3) / sw.Elapsed.TotalSeconds;
            Console.WriteLine($"Bishop attacks: {bishopPerSec,12:N0} lookups/sec");

            // Benchmark rook attacks
            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                foreach (var occ in occupancies)
                {
                    MagicBitboards.GetRookAttacks(27, occ); // d4
                    MagicBitboards.GetRookAttacks(0, occ);  // a1
                    MagicBitboards.GetRookAttacks(63, occ); // h8
                }
            }
            sw.Stop();

            double rookPerSec = (iterations * occupancies.Length * 3) / sw.Elapsed.TotalSeconds;
            Console.WriteLine($"Rook attacks:   {rookPerSec,12:N0} lookups/sec");

            // Benchmark queen attacks
            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                foreach (var occ in occupancies)
                {
                    MagicBitboards.GetQueenAttacks(27, occ); // d4
                    MagicBitboards.GetQueenAttacks(0, occ);  // a1
                    MagicBitboards.GetQueenAttacks(63, occ); // h8
                }
            }
            sw.Stop();

            double queenPerSec = (iterations * occupancies.Length * 3) / sw.Elapsed.TotalSeconds;
            Console.WriteLine($"Queen attacks:  {queenPerSec,12:N0} lookups/sec");
            Console.WriteLine();
        }

        private static void BenchmarkMemoryPressure()
        {
            Console.WriteLine("Memory Pressure Test:");
            Console.WriteLine("--------------------");

            var board = FenParser.ParseFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");

            long memBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            // Generate moves many times to check for allocations
            const int iterations = 10_000_000;
            for (int i = 0; i < iterations; i++)
            {
                var moves = new MoveList();
                MoveGenerator.GenerateMoves(ref board, ref moves);
            }

            sw.Stop();
            long memAfter = GC.GetTotalMemory(false);

            double memPerIteration = (memAfter - memBefore) / (double)iterations;
            Console.WriteLine($"Memory allocated per generation: {memPerIteration:F2} bytes");
            Console.WriteLine($"GC collections: Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}");
            Console.WriteLine();
        }

        private static void BenchmarkDifferentPositions()
        {
            Console.WriteLine("Position-Specific Performance:");
            Console.WriteLine("-----------------------------");

            var positions = new[]
            {
            ("Opening", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"),
            ("Italian Game", "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4"),
            ("Sicilian", "r1bqkbnr/pp1ppppp/2n5/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3"),
            ("Closed", "r1bqr1k1/1p1nbppp/p1pp1n2/4p3/3PP3/2N1BN2/PPP1BPPP/R2Q1RK1 w - - 0 11"),
            ("Tactical", "r1b1k2r/ppppnppp/2n2q2/2b5/3NP3/2P1B3/PP3PPP/RN1QKB1R w KQkq - 0 7"),
            ("Endgame K+P", "8/8/1p6/p1p5/P1P5/1P6/8/4K2k w - - 0 1"),
            ("Endgame R+P", "8/5pk1/6p1/7p/5P1P/6P1/r7/3R3K b - - 0 1"),
            ("Promotion", "8/PPP4k/8/8/8/8/4Kppp/8 w - - 0 1")
        };

            foreach (var (name, fen) in positions)
            {
                var board = FenParser.ParseFen(fen);

                var sw = Stopwatch.StartNew();
                long nodes = Perft.RunPerft(board, 5);
                sw.Stop();

                double nps = nodes / sw.Elapsed.TotalSeconds;
                Console.WriteLine($"{name,-15} Perft(5): {nodes,10:N0} nodes, {nps,12:N0} NPS");
            }
        }
    }
}
