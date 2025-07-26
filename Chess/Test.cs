using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chess
{
    public static class Tests
    {
        private static int _testsPassed = 0;
        private static int _testsFailed = 0;

        public static void RunAllTests()
        {
            Console.WriteLine("=== CHESS ENGINE CORRECTNESS TESTS ===\n");

            var sw = Stopwatch.StartNew();

            // Reset counters
            _testsPassed = 0;
            _testsFailed = 0;

            // Run all test suites
            TestPerft();
            TestMoveGeneration();
            TestSpecialMoves();
            TestBoardManipulation();
            TestAttackDetection();
            TestFenParsing();
            TestEdgeCases();
            TestMoveEncoding();

            sw.Stop();

            // Summary
            Console.WriteLine("\n=== TEST SUMMARY ===");
            Console.WriteLine($"Total tests: {_testsPassed + _testsFailed}");
            Console.WriteLine($"Passed: {_testsPassed} ✓");
            Console.WriteLine($"Failed: {_testsFailed} ✗");
            Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"Result: {(_testsFailed == 0 ? "ALL TESTS PASSED! 🎉" : "SOME TESTS FAILED! 🔥")}");
        }

        private static void TestPerft()
        {
            Console.WriteLine("Testing Perft (Move Generation Correctness)...");

            var perftTests = new[]
            {
            // Starting position
            ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 1, 20L),
            ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 2, 400L),
            ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 3, 8902L),
            ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 4, 197281L),
            
            // Kiwipete - tests all move types
            ("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 1, 48L),
            ("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 2, 2039L),
            ("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 3, 97862L),
            
            // Position 3
            ("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 4, 43238L),
            
            // Position 4 - Promotions and captures
            ("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9467L),
            
            // Position 5 - Promotion bugs
            ("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62379L),
            
            // En passant test
            ("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3", 2, 615L),
        };

            foreach ((string fen, int depth, long expected) in perftTests)
            {
                var board = FenParser.ParseFen(fen);
                long result = Perft.RunPerft(board, depth);
                AssertEqual($"Perft({depth}) for {fen.Split(' ')[0]}", expected, result);
            }
        }

        private static void TestMoveGeneration()
        {
            Console.WriteLine("\nTesting Move Generation for Specific Positions...");

            // Test starting position
            var board = Board.StartingPosition();
            var moves = new MoveList();
            MoveGenerator.GenerateMoves(ref board, ref moves);
            AssertEqual("Starting position move count", 20, moves.Count);

            // Test position with only kings
            board = FenParser.ParseFen("8/8/8/3k4/8/3K4/8/8 w - - 0 1");
            moves.Clear();
            MoveGenerator.GenerateMoves(ref board, ref moves);
            AssertEqual("King vs King move count", 5, moves.Count);

            // Test stalemate position
            board = FenParser.ParseFen("8/8/8/8/8/5k2/5p2/5K2 w - - 0 1");
            moves.Clear();
            MoveGenerator.GenerateMoves(ref board, ref moves);
            AssertEqual("Stalemate position move count", 0, moves.Count);
        }

        private static void TestSpecialMoves()
        {
            Console.WriteLine("\nTesting Special Moves...");

            // Test castling
            var board = FenParser.ParseFen("r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1");
            var moves = new MoveList();
            MoveGenerator.GenerateMoves(ref board, ref moves);

            bool hasKingsideCastle = false;
            bool hasQueensideCastle = false;

            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].IsCastling && moves[i].From == 4 && moves[i].To == 6)
                    hasKingsideCastle = true;
                if (moves[i].IsCastling && moves[i].From == 4 && moves[i].To == 2)
                    hasQueensideCastle = true;
            }

            Assert("White kingside castling available", hasKingsideCastle);
            Assert("White queenside castling available", hasQueensideCastle);

            // Test en passant
            board = FenParser.ParseFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
            moves.Clear();
            MoveGenerator.GenerateMoves(ref board, ref moves);

            bool hasEnPassant = false;
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].IsEnPassant)
                    hasEnPassant = true;
            }

            Assert("En passant capture available", hasEnPassant);

            // Test promotions
            board = FenParser.ParseFen("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");
            moves.Clear();
            MoveGenerator.GenerateMoves(ref board, ref moves);

            int promotionCount = 0;
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].IsPromotion)
                    promotionCount++;
            }

            AssertEqual("Promotion moves count", 4, promotionCount); // Q, R, B, N
        }

        private static void TestBoardManipulation()
        {
            Console.WriteLine("\nTesting Board Manipulation...");

            var board = Board.StartingPosition();

            // Test piece retrieval
            var (piece, color) = board.GetPieceAt(0); // a1
            Assert("Rook on a1", piece == PieceType.Rook && color == Color.White);

            (piece, color) = board.GetPieceAt(4); // e1
            Assert("King on e1", piece == PieceType.King && color == Color.White);

            (piece, color) = board.GetPieceAt(60); // e8
            Assert("King on e8", piece == PieceType.King && color == Color.Black);

            // Test empty square
            (piece, color) = board.GetPieceAt(35); // d4
            Assert("Empty square d4", piece == PieceType.None);

            // Test making moves
            var originalBoard = board;
            var e2e4 = new Move(12, 28, MoveFlags.DoublePush); // e2-e4
            board.MakeMove(e2e4);

            Assert("Side to move changed", board.SideToMove == Color.Black);
            AssertEqual("En passant square set", 20, board.EnPassantSquare); // e3

            (piece, color) = board.GetPieceAt(28); // e4
            Assert("Pawn moved to e4", piece == PieceType.Pawn && color == Color.White);

            (piece, color) = board.GetPieceAt(12); // e2
            Assert("e2 is now empty", piece == PieceType.None);
        }

        private static void TestAttackDetection()
        {
            Console.WriteLine("\nTesting Attack Detection...");

            // Test pawn attacks
            var board = FenParser.ParseFen("4k3/8/8/3p4/2P5/8/8/4K3 w - - 0 1");
            Assert("Black pawn attacks c4", board.IsSquareAttacked(26, Color.Black)); // c4
            Assert("White pawn attacks d5", board.IsSquareAttacked(35, Color.White)); // d5

            // Test knight attacks
            board = FenParser.ParseFen("4k3/8/3n4/8/8/8/8/4K3 w - - 0 1");
            Assert("Knight attacks e4", board.IsSquareAttacked(28, Color.Black)); // e4
            Assert("Knight attacks f5", board.IsSquareAttacked(37, Color.Black)); // f5

            // Test bishop attacks
            board = FenParser.ParseFen("4k3/8/8/8/3B4/8/8/4K3 w - - 0 1");
            Assert("Bishop attacks a1", board.IsSquareAttacked(0, Color.White)); // a1
            Assert("Bishop attacks h8", board.IsSquareAttacked(63, Color.White)); // h8

            // Test rook attacks
            board = FenParser.ParseFen("4k3/8/8/8/3R4/8/8/4K3 w - - 0 1");
            Assert("Rook attacks d1", board.IsSquareAttacked(3, Color.White)); // d1
            Assert("Rook attacks h4", board.IsSquareAttacked(31, Color.White)); // h4

            // Test check detection
            board = FenParser.ParseFen("4k3/8/8/8/8/8/4R3/4K3 b - - 0 1");
            Assert("Black king in check", board.IsInCheckFast());
        }

        private static void TestFenParsing()
        {
            Console.WriteLine("\nTesting FEN Parsing...");

            var testFens = new[]
            {
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
            "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
            "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8"
        };

            for (int i = 0; i<testFens.Length; i++)
            {
                string? fen = testFens[i];
                try
                {
                    var board = FenParser.ParseFen(fen);
                    Assert($"FEN parsing: {fen.Split(' ')[0]}", true);
                }
                catch
                {
                    Assert($"FEN parsing: {fen.Split(' ')[0]}", false);
                }
            }
        }

        private static void TestEdgeCases()
        {
            Console.WriteLine("\nTesting Edge Cases...");

            // Test position with maximum possible moves
            var board = FenParser.ParseFen("R6R/3Q4/1Q4Q1/4Q3/2Q4Q/Q4Q2/pp1Q4/kBNN1KB1 w - - 0 1");
            var moves = new MoveList();
            MoveGenerator.GenerateMoves(ref board, ref moves);
            Assert("High mobility position generates moves", moves.Count > 200);

            // Test illegal castling through check
            board = FenParser.ParseFen("r3k2r/8/8/8/4R3/8/8/R3K2R b KQkq - 0 1");
            moves.Clear();
            MoveGenerator.GenerateMoves(ref board, ref moves);

            bool hasIllegalCastle = false;
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].IsCastling)
                    hasIllegalCastle = true;
            }

            Assert("No castling through check", !hasIllegalCastle);

            // Test en passant creating discovered check (should be illegal)
            board = FenParser.ParseFen("8/8/8/2k2pP1/8/8/8/4K2R b - g6 0 1");
            moves.Clear();
            MoveGenerator.GenerateMoves(ref board, ref moves);

            bool hasEnPassant = false;
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].IsEnPassant)
                    hasEnPassant = true;
            }

            Assert("No en passant that exposes king", !hasEnPassant);
        }

        private static void TestMoveEncoding()
        {
            Console.WriteLine("\nTesting Move Encoding...");

            // Test all promotion types encode/decode correctly
            var promotionTypes = new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight };

            foreach (var promo in promotionTypes)
            {
                var move = new Move(48, 56, MoveFlags.None, promo); // a7-a8
                AssertEqual($"Promotion to {promo}", promo, move.Promotion);
            }

            // Test move with all flags
            var complexMove = new Move(12, 20, MoveFlags.Capture | MoveFlags.EnPassant);
            Assert("Complex move is capture", complexMove.IsCapture);
            Assert("Complex move is en passant", complexMove.IsEnPassant);
            Assert("Complex move is not castling", !complexMove.IsCastling);

            // Test move string representation
            var e2e4 = new Move(12, 28);
            AssertEqual("Move toString", "e2e4", e2e4.ToString());

            var promoMove = new Move(48, 56, MoveFlags.None, PieceType.Queen);
            AssertEqual("Promotion toString", "a7a8q", promoMove.ToString());
        }

        // Helper methods
        private static void Assert(string testName, bool condition)
        {
            if (condition)
            {
                Console.WriteLine($"  ✓ {testName}");
                _testsPassed++;
            }
            else
            {
                Console.WriteLine($"  ✗ {testName} FAILED!");
                _testsFailed++;
            }
        }

        private static void AssertEqual<T>(string testName, T expected, T actual)
        {
            bool passed = expected.Equals(actual);
            if (passed)
            {
                Console.WriteLine($"  ✓ {testName}");
                _testsPassed++;
            }
            else
            {
                Console.WriteLine($"  ✗ {testName} FAILED! Expected: {expected}, Got: {actual}");
                _testsFailed++;
            }
        }
    }
}
