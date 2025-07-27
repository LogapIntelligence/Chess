using Move;
using System;
using System.Diagnostics;

namespace Test
{
    public static class Perft
    {
        /// <summary>
        /// Run perft on a single position and return the node count
        /// </summary>
        public static unsafe ulong RunSingle(Position position, uint depth)
        {
            return PerftOptimized.Run(position, depth);
        }

        /// <summary>
        /// Run perft on a FEN string and return the node count
        /// </summary>
        public static unsafe ulong RunSingle(string fen, uint depth)
        {
            var position = new Position();
            Position.Set(fen, position);
            return RunSingle(position, depth);
        }

        /// <summary>
        /// Run benchmark suite (called by UCI bench command)
        /// </summary>
        public static void RunBenchmark()
        {
            Console.WriteLine("CE3 Benchmark");
            Console.WriteLine("=============");

            var totalNodes = 0UL;
            var totalTime = 0L;
            var sw = new Stopwatch();

            // Standard benchmark positions with specific depths
            var benchPositions = new[]
            {
                ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 5), // Starting position
                ("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -", 4), // Kiwipete
                ("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - -", 5), // Endgame position
                ("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 4), // Complex middle game
                ("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 4), // Position with checks
                ("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 4), // Symmetrical position
            };

            foreach (var (fen, depth) in benchPositions)
            {
                var position = new Position();
                Position.Set(fen, position);

                sw.Restart();
                var nodes = RunSingle(position, (uint)depth);
                sw.Stop();

                totalNodes += nodes;
                totalTime += sw.ElapsedMilliseconds;

                Console.WriteLine($"Position: {fen.Substring(0, Math.Min(50, fen.Length))}...");
                Console.WriteLine($"  Depth {depth}: {nodes,12:N0} nodes [{sw.ElapsedMilliseconds}ms]");
            }

            Console.WriteLine();
            Console.WriteLine($"Total nodes: {totalNodes:N0}");
            Console.WriteLine($"Total time: {totalTime}ms");

            if (totalTime > 0)
            {
                var nps = totalNodes * 1000 / (ulong)totalTime;
                Console.WriteLine($"Nodes/second: {nps:N0}");
            }
        }

        /// <summary>
        /// Original comprehensive test suite
        /// </summary>
        public static unsafe void Run()
        {
            Console.WriteLine("Running Perft Test Suite");
            Console.WriteLine("========================\n");

            int totalTests = 0;
            int passedTests = 0;
            long totalTime = 0;
            ulong totalNodes = 0;

            // Test each position
            foreach (var testPosition in Positions.Perfs)
            {
                Console.WriteLine($"Testing: {testPosition.Title}");
                Console.WriteLine($"FEN: {testPosition.FEN}");

                var p = new Position();
                Position.Set(testPosition.FEN, p);

                // Get expected values for this position
                var expectedValues = new[]
                {
                    (1U, (ulong)testPosition.P1),
                    (2U, (ulong)testPosition.P2),
                    (3U, (ulong)testPosition.P3),
                    (4U, (ulong)testPosition.P4),
                    (5U, (ulong)testPosition.P5),
                    (6U, (ulong)testPosition.P6),
                    (7U, (ulong)testPosition.P7)
                };

                bool positionPassed = true;
                int maxDepthToTest = DetermineMaxDepth(testPosition.Title);

                foreach (var (depth, expected) in expectedValues)
                {
                    if (depth > maxDepthToTest) break;
                    if (expected == 0) continue; // Skip if no expected value

                    totalTests++;
                    var sw = Stopwatch.StartNew();
                    ulong nodes = RunSingle(p, depth);
                    sw.Stop();

                    bool passed = nodes == expected;
                    if (passed) passedTests++;
                    else positionPassed = false;

                    string status = passed ? "✓" : "✗";
                    string timeStr = sw.ElapsedMilliseconds > 1000
                        ? $"{sw.ElapsedMilliseconds / 1000.0:F1}s"
                        : $"{sw.ElapsedMilliseconds}ms";

                    Console.WriteLine($"  Depth {depth}: {status} {nodes,12:N0} nodes (expected {expected,12:N0}) [{timeStr}]");

                    if (depth == 5 || depth == 6) // Track performance on deeper searches
                    {
                        totalTime += sw.ElapsedMilliseconds;
                        totalNodes += nodes;
                    }
                }

                Console.WriteLine($"  Position Result: {(positionPassed ? "PASSED" : "FAILED")}");
                Console.WriteLine();
            }

            // Summary
            Console.WriteLine("Test Summary");
            Console.WriteLine("============");
            Console.WriteLine($"Total Tests: {totalTests}");
            Console.WriteLine($"Passed: {passedTests}");
            Console.WriteLine($"Failed: {totalTests - passedTests}");
            Console.WriteLine($"Success Rate: {(double)passedTests / totalTests * 100:F1}%");

            if (totalTime > 0 && totalNodes > 0)
            {
                double nps = totalNodes / (totalTime / 1000.0);
                Console.WriteLine($"\nPerformance: {nps:N0} nodes/second (avg from depth 5-6 tests)");
            }

            // Warmup and benchmark with standard position
            Console.WriteLine("\nRunning benchmark on standard position (depth 7)...");
            var benchPos = new Position();
            Position.Set("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -", benchPos);

            // Warmup
            for (int i = 0; i < 5; i++)
                _ = RunSingle(benchPos, 4);

            var benchSw = Stopwatch.StartNew();
            ulong benchNodes = RunSingle(benchPos, 7);
            benchSw.Stop();

            double benchNps = benchNodes / (benchSw.ElapsedMilliseconds / 1000.0);
            Console.WriteLine($"Benchmark: {benchNodes:N0} nodes in {benchSw.ElapsedMilliseconds / 1000.0:F2}s");
            Console.WriteLine($"Speed: {benchNps:N0} nodes/second");
        }

        private static int DetermineMaxDepth(string title)
        {
            // Set reasonable depth limits based on position complexity
            return title.ToLower() switch
            {
                var t when t.Contains("standard") => 6,
                var t when t.Contains("major pieces") => 5,
                var t when t.Contains("promotion") => 6,
                _ => 6
            };
        }
    }
}