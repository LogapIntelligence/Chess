using Move;
using System;
using System.Threading;
using System.Threading.Tasks;
using Test;
using static System.Net.Mime.MediaTypeNames;
using File = Move.File;

namespace Search
{
    public class UCI
    {
        private readonly Search search;
        private Position position;
        private Task? searchTask;
        private CancellationTokenSource? searchCancellation;
        private readonly object positionLock = new object();

        public UCI()
        {
            // Initialize tables first
            Tables.Init();
            Zobrist.Init();

            search = new Search(128);
            position = new Position();
            Position.Set(Types.DEFAULT_FEN, position);
        }

        public async Task Run()
        {
            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "uci":
                        HandleUci();
                        break;

                    case "isready":
                        SendCommand("readyok");
                        break;

                    case "ucinewgame":
                        await HandleNewGame();
                        break;

                    case "position":
                        await HandlePosition(parts);
                        break;

                    case "go":
                        _ = HandleGo(parts);
                        break;

                    case "stop":
                        await HandleStop();
                        break;

                    case "quit":
                        await HandleStop();
                        return;

                    case "d":
                    case "display":
                        lock (positionLock)
                        {
                            Console.WriteLine(position);
                        }
                        break;

                    case "perft":
                        if (parts.Length > 1 && int.TryParse(parts[1], out int depth))
                            HandlePerft(depth);
                        break;

                    case "bench":
                        HandleBench();
                        break;
                }
            }
        }

        private void HandleUci()
        {
            SendCommand("id name GORB2");
            SendCommand("id author Assistant");
            SendCommand("option name Hash type spin default 128 min 1 max 16384");
            SendCommand("option name Threads type spin default 1 min 1 max 1");
            SendCommand("uciok");
        }

        private async Task HandleNewGame()
        {
            // Stop any ongoing search
            await HandleStop();

            lock (positionLock)
            {
                position = new Position();
                Position.Set(Types.DEFAULT_FEN, position);
            }
        }

        private async Task HandlePosition(string[] parts)
        {
            // CRITICAL: Stop any ongoing search before updating position
            await HandleStop();

            lock (positionLock)
            {
                var movesIndex = Array.IndexOf(parts, "moves");

                if (parts.Length > 1 && parts[1] == "startpos")
                {
                    position = new Position();
                    Position.Set(Types.DEFAULT_FEN, position);
                }
                else if (parts.Length > 1 && parts[1] == "fen")
                {
                    // Reconstruct FEN string
                    var fenParts = new string[movesIndex > 0 ? movesIndex - 2 : parts.Length - 2];
                    Array.Copy(parts, 2, fenParts, 0, fenParts.Length);
                    var fen = string.Join(" ", fenParts);

                    position = new Position();
                    Position.Set(fen, position);
                }

                // Apply moves
                if (movesIndex > 0)
                {
                    for (int i = movesIndex + 1; i < parts.Length; i++)
                    {
                        var move = ParseMove(parts[i]);
                        if (move.From != move.To)
                        {
                            position.Play(position.Turn, move);
                        }
                    }
                }
            }
        }

        private void SendCommand(string command)
        {
            Console.WriteLine(command);
            Console.Out.Flush();
        }

        private async Task HandleGo(string[] parts)
        {
            // Check if this is a perft command
            if (parts.Length > 1 && parts[1] == "perft")
            {
                if (parts.Length > 2 && int.TryParse(parts[2], out int perftDepth))
                {
                    HandlePerft(perftDepth);
                }
                return;
            }

            var limits = new SearchLimits();
            bool hasTimeControl = false;

            for (int i = 1; i < parts.Length; i++)
            {
                switch (parts[i])
                {
                    case "depth":
                        if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int depth))
                        {
                            limits.Depth = depth;
                            hasTimeControl = true;
                            i++;
                        }
                        break;

                    case "movetime":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long moveTime))
                        {
                            limits.MoveTime = moveTime;
                            hasTimeControl = true;
                            i++;
                        }
                        break;

                    case "wtime":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long wtime) &&
                            position.Turn == Color.White)
                        {
                            limits.Time = wtime;
                            hasTimeControl = true;
                            i++;
                        }
                        break;

                    case "btime":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long btime) &&
                            position.Turn == Color.Black)
                        {
                            limits.Time = btime;
                            hasTimeControl = true;
                            i++;
                        }
                        break;

                    case "winc":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long winc) &&
                            position.Turn == Color.White)
                        {
                            limits.Inc = winc;
                            i++;
                        }
                        break;

                    case "binc":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long binc) &&
                            position.Turn == Color.Black)
                        {
                            limits.Inc = binc;
                            i++;
                        }
                        break;

                    case "movestogo":
                        if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int mtg))
                        {
                            limits.MovesToGo = mtg;
                            i++;
                        }
                        break;

                    case "infinite":
                        limits.Infinite = true;
                        hasTimeControl = true;
                        break;
                }
            }

            // If no time control specified and not infinite, set reasonable defaults
            if (!hasTimeControl)
            {
                limits.Depth = 12;
                limits.MoveTime = 5000;
            }

            // REMOVED: await HandleStop();
            // This call is redundant. The UCI protocol guarantees that a `position`, `stop`,
            // or `ucinewgame` command will be sent before a new `go` command, and those
            // handlers already correctly stop any ongoing search.

            // Create a copy of the position for the search thread
            Position searchPosition;
            lock (positionLock)
            {
                searchPosition = new Position(position);
            }

            // Start new search
            searchCancellation = new CancellationTokenSource();
            var cancellationToken = searchCancellation.Token;

            searchTask = Task.Run(() =>
            {
                try
                {
                    var result = search.StartSearch(searchPosition, limits);

                    // Only output bestmove if not cancelled
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        SendCommand($"bestmove {result.BestMove}");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Search was cancelled, don't output anything
                }
                catch (Exception ex)
                {
                    // Log any unexpected errors
                    Console.Error.WriteLine($"Search error: {ex.Message}");
                }
            }, cancellationToken);

            // REMOVED: The entire block that awaits the searchTask.
            // The HandleGo method now returns immediately after starting the search,
            // allowing the main UCI loop to process other commands like "stop".
        }

        private async Task HandleStop()
        {
            if (searchTask != null)
            {
                // First tell the search to stop
                search.StopSearch();

                // Then cancel the task if we have a cancellation token
                searchCancellation?.Cancel();

                try
                {
                    // Wait for the task to complete with a timeout
                    if (searchTask != null)
                    {
                        await searchTask.WaitAsync(TimeSpan.FromSeconds(2));
                    }
                }
                catch (TimeoutException)
                {
                    // If it doesn't stop in time, we'll continue anyway
                    Console.Error.WriteLine("Warning: Search didn't stop in time");
                }
                catch (OperationCanceledException)
                {
                    // This is expected
                }

                // Clean up
                searchCancellation?.Dispose();
                searchCancellation = null;
                searchTask = null;
            }
        }

        private Move.Move ParseMove(string moveStr)
        {
            if (moveStr.Length < 4)
                return new Move.Move(); // Invalid move

            var from = ParseSquare(moveStr.Substring(0, 2));
            var to = ParseSquare(moveStr.Substring(2, 2));

            if (from == Square.NoSquare || to == Square.NoSquare)
                return new Move.Move(); // Invalid move

            // Check for promotion
            if (moveStr.Length == 5)
            {
                var promoPiece = moveStr[4];
                var flags = MoveFlags.Quiet;

                // Check if it's a capture by looking at the current position
                if (position.At(to) != Piece.NoPiece)
                {
                    // Promotion capture
                    switch (promoPiece)
                    {
                        case 'q': flags = MoveFlags.PcQueen; break;
                        case 'r': flags = MoveFlags.PcRook; break;
                        case 'b': flags = MoveFlags.PcBishop; break;
                        case 'n': flags = MoveFlags.PcKnight; break;
                        default: return new Move.Move(); // Invalid promotion
                    }
                }
                else
                {
                    // Quiet promotion
                    switch (promoPiece)
                    {
                        case 'q': flags = MoveFlags.PrQueen; break;
                        case 'r': flags = MoveFlags.PrRook; break;
                        case 'b': flags = MoveFlags.PrBishop; break;
                        case 'n': flags = MoveFlags.PrKnight; break;
                        default: return new Move.Move(); // Invalid promotion
                    }
                }

                return new Move.Move(from, to, flags);
            }

            // Generate all legal moves to find the correct move with proper flags
            var moves = new Move.Move[256];
            var moveCount = GenerateMovesForPosition(moves);

            // Find the matching move
            for (int i = 0; i < moveCount; i++)
            {
                if (moves[i].From == from && moves[i].To == to)
                {
                    return moves[i];
                }
            }

            // If no matching legal move found, create a basic move
            // This shouldn't happen with valid UCI input
            return new Move.Move(from, to);
        }

        private unsafe int GenerateMovesForPosition(Move.Move[] moveBuffer)
        {
            fixed (Move.Move* movesPtr = moveBuffer)
            {
                if (position.Turn == Color.White)
                    return position.GenerateLegalsInto<White>(movesPtr);
                else
                    return position.GenerateLegalsInto<Black>(movesPtr);
            }
        }

        private Square ParseSquare(string sq)
        {
            if (sq.Length != 2)
                return Square.NoSquare;

            var file = sq[0] - 'a';
            var rank = sq[1] - '1';

            if (file < 0 || file > 7 || rank < 0 || rank > 7)
                return Square.NoSquare;

            return Types.CreateSquare((File)file, (Rank)rank);
        }

        private void HandlePerft(int depth)
        {
            var fen = position.Fen();
            Console.WriteLine($"Running perft depth {depth} on position:");
            Console.WriteLine($"FEN: {fen}");
            Console.WriteLine();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = Test.Perft.RunSingle(position, (uint)depth);
            sw.Stop();

            Console.WriteLine($"Nodes searched: {result:N0}");
            Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");

            if (sw.ElapsedMilliseconds > 0)
            {
                var nps = result * 1000 / (ulong)sw.ElapsedMilliseconds;
                Console.WriteLine($"Nodes/second: {nps:N0}");
            }
        }

        private void HandleBench()
        {
            Console.WriteLine("Running benchmark suite...");
            Console.WriteLine();
            Perft.RunBenchmark();
        }
    }
}