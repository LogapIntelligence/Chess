using Move;
using System;
using System.Diagnostics;
namespace Test;
public static class Perft
{
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
                ulong nodes = PerftOptimized.Run(p, depth);
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
            _ = PerftOptimized.Run(benchPos, 4);

        var benchSw = Stopwatch.StartNew();
        ulong benchNodes = PerftOptimized.Run(benchPos, 7);
        benchSw.Stop();

        double benchNps = benchNodes / (benchSw.ElapsedMilliseconds / 1000.0);
        Console.WriteLine($"Benchmark: {benchNodes:N0} nodes in {benchSw.ElapsedMilliseconds / 1000.0:F2}s");
        Console.WriteLine($"Speed: {benchNps:N0} nodes/second");
    }

    private static int DetermineMaxDepth(string title)
    {
        return 6;
    }
}