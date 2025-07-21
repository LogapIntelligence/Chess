namespace Chess;

using System.Diagnostics;

public static class Perft
{
    // Standard perft positions with expected node counts
    public static readonly (string fen, int depth, long expected)[] TestPositions = new[]
    {
        // Starting position
        ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 1, 20L),
        ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 2, 400L),
        ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 3, 8902L),
        ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 4, 197281L),
        ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 5, 4865609L),
        
        // Kiwipete position (complex position with all move types)
        ("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 1, 48L),
        ("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 2, 2039L),
        ("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 3, 97862L),
        ("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 4, 4085603L),
        
        // Position 3
        ("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 1, 14L),
        ("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 2, 191L),
        ("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 3, 2812L),
        ("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 4, 43238L),
        ("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 5, 674624L),
        
        // Position 4
        ("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 1, 6L),
        ("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 2, 264L),
        ("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9467L),
        ("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 4, 422333L),
        
        // Position 5 (promotion bugs)
        ("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 1, 44L),
        ("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 2, 1486L),
        ("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62379L),
        ("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 4, 2103487L),
    };

    public static long RunPerft(Board board, int depth)
    {
        if (depth == 0) return 1;

        MoveList moves = new();
        MoveGenerator.GenerateMoves(ref board, ref moves);

        if (depth == 1) return moves.Count;

        long nodes = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            Board copy = board;
            copy.MakeMove(moves[i]);
            nodes += RunPerft(copy, depth - 1);
        }

        return nodes;
    }

    public static void RunPerftDivide(Board board, int depth)
    {
        MoveList moves = new();
        MoveGenerator.GenerateMoves(ref board, ref moves);

        long totalNodes = 0;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < moves.Count; i++)
        {
            Board copy = board;
            copy.MakeMove(moves[i]);
            long nodes = depth == 1 ? 1 : RunPerft(copy, depth - 1);
            totalNodes += nodes;
            Console.WriteLine($"{moves[i]}: {nodes}");
        }

        sw.Stop();
        double nps = totalNodes / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"\nTotal: {totalNodes} nodes");
        Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"NPS: {nps:N0}");
    }

    public static void RunTests()
    {
        Console.WriteLine("Running Perft Tests...\n");

        foreach (var (fen, depth, expected) in TestPositions)
        {
            var board = FenParser.ParseFen(fen);
            var sw = Stopwatch.StartNew();
            long result = RunPerft(board, depth);
            sw.Stop();

            bool passed = result == expected;
            double nps = result / sw.Elapsed.TotalSeconds;

            Console.WriteLine($"Position: {fen}");
            Console.WriteLine($"Depth: {depth}, Expected: {expected}, Got: {result}");
            Console.WriteLine($"Status: {(passed ? "PASSED" : "FAILED")}");
            Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms, NPS: {nps:N0}");
            Console.WriteLine();

            if (!passed) break;
        }
    }

    public static void BenchmarkMoveGeneration(int iterations = 1000000)
    {
        var board = Board.StartingPosition();
        MoveList moves = new();

        // Warmup
        for (int i = 0; i < 1000; i++)
        {
            MoveGenerator.GenerateMoves(ref board, ref moves);
        }

        var sw = Stopwatch.StartNew();
        long totalMoves = 0;

        for (int i = 0; i < iterations; i++)
        {
            moves.Clear();
            MoveGenerator.GenerateMoves(ref board, ref moves);
            totalMoves += moves.Count;
        }

        sw.Stop();
        double movesPerSecond = iterations / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"\nMove Generation Benchmark:");
        Console.WriteLine($"Iterations: {iterations:N0}");
        Console.WriteLine($"Total time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Positions/second: {movesPerSecond:N0}");
        Console.WriteLine($"Average moves per position: {totalMoves / (double)iterations:F1}");
        Console.WriteLine($"Time per position: {sw.Elapsed.TotalMicroseconds / iterations:F2}μs");
    }
}

public static class FenParser
{
    public static Board ParseFen(string fen)
    {
        var parts = fen.Split(' ');
        var board = new Board();

        // Parse piece placement
        int rank = 7;
        int file = 0;

        foreach (char c in parts[0])
        {
            if (c == '/')
            {
                rank--;
                file = 0;
            }
            else if (char.IsDigit(c))
            {
                file += c - '0';
            }
            else
            {
                int square = rank * 8 + file;
                ulong bit = 1UL << square;

                switch (c)
                {
                    case 'P': board.WhitePawns |= bit; break;
                    case 'N': board.WhiteKnights |= bit; break;
                    case 'B': board.WhiteBishops |= bit; break;
                    case 'R': board.WhiteRooks |= bit; break;
                    case 'Q': board.WhiteQueens |= bit; break;
                    case 'K': board.WhiteKing |= bit; break;
                    case 'p': board.BlackPawns |= bit; break;
                    case 'n': board.BlackKnights |= bit; break;
                    case 'b': board.BlackBishops |= bit; break;
                    case 'r': board.BlackRooks |= bit; break;
                    case 'q': board.BlackQueens |= bit; break;
                    case 'k': board.BlackKing |= bit; break;
                }
                file++;
            }
        }

        // Side to move
        board.SideToMove = parts[1] == "w" ? Color.White : Color.Black;

        // Castling rights
        board.CastlingRights = CastlingRights.None;
        if (parts[2].Contains('K')) board.CastlingRights |= CastlingRights.WhiteKingside;
        if (parts[2].Contains('Q')) board.CastlingRights |= CastlingRights.WhiteQueenside;
        if (parts[2].Contains('k')) board.CastlingRights |= CastlingRights.BlackKingside;
        if (parts[2].Contains('q')) board.CastlingRights |= CastlingRights.BlackQueenside;

        // En passant
        if (parts[3] != "-")
        {
            int epFile = parts[3][0] - 'a';
            int epRank = parts[3][1] - '1';
            board.EnPassantSquare = epRank * 8 + epFile;
        }
        else
        {
            board.EnPassantSquare = -1;
        }

        // Halfmove clock
        board.HalfmoveClock = int.Parse(parts[4]);

        // Fullmove number
        board.FullmoveNumber = int.Parse(parts[5]);

        board.UpdateAggregateBitboards();
        return board;
    }
}