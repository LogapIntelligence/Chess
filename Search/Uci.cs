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
        private volatile bool isSearching = false;

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
                try
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
                            await HandleGo(parts);
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
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"UCI Command Error: {ex.Message}");
                    // Continue processing other commands
                }
            }
        }

        private void HandleUci()
        {
            SendCommand("id name Blibnuts");
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
            // CRITICAL: Always stop search before position updates
            if (isSearching)
            {
                await HandleStop();
            }

            lock (positionLock)
            {
                try
                {
                    var movesIndex = Array.IndexOf(parts, "moves");

                    if (parts.Length > 1 && parts[1] == "startpos")
                    {
                        position = new Position();
                        Position.Set(Types.DEFAULT_FEN, position);
                    }
                    else if (parts.Length > 1 && parts[1] == "fen")
                    {
                        // Reconstruct FEN string - be more careful about bounds
                        var fenEndIndex = movesIndex > 0 ? movesIndex : parts.Length;
                        if (fenEndIndex - 2 <= 0)
                        {
                            Console.Error.WriteLine("Invalid FEN command format");
                            return;
                        }

                        var fenParts = new string[fenEndIndex - 2];
                        Array.Copy(parts, 2, fenParts, 0, fenParts.Length);
                        var fen = string.Join(" ", fenParts);

                        position = new Position();
                        Position.Set(fen, position);
                    }

                    // Apply moves
                    if (movesIndex > 0 && movesIndex + 1 < parts.Length)
                    {
                        for (int i = movesIndex + 1; i < parts.Length; i++)
                        {
                            var move = ParseMove(parts[i]);
                            if (move.From != move.To)
                            {
                                position.Play(position.Turn, move);
                            }
                            else
                            {
                                Console.Error.WriteLine($"Invalid move: {parts[i]} in position {position.Fen()}");
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Position parsing error: {ex.Message}");
                    // Reset to default position on error
                    position = new Position();
                    Position.Set(Types.DEFAULT_FEN, position);
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

            // Stop any ongoing search first
            if (isSearching)
            {
                await HandleStop();
            }

            var limits = ParseGoCommand(parts);

            // Create a copy of the position for the search thread
            Position searchPosition;
            lock (positionLock)
            {
                searchPosition = new Position(position);
            }

            // Start new search
            isSearching = true;
            searchCancellation = new CancellationTokenSource();
            var cancellationToken = searchCancellation.Token;

            searchTask = Task.Run(async () =>
            {
                try
                {
                    var result = search.StartSearch(searchPosition, limits);

                    // Always output bestmove unless explicitly cancelled
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        SendCommand($"bestmove {result.BestMove}");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Search was cancelled - output current best move if available
                    SendCommand("bestmove 0000");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Search error: {ex.Message}");
                    // Always output something to prevent GUI hanging
                    SendCommand("bestmove 0000");
                }
                finally
                {
                    isSearching = false;
                }
            }, cancellationToken);

            // Don't await - return immediately to continue processing UCI commands
        }

        private SearchLimits ParseGoCommand(string[] parts)
        {
            var limits = new SearchLimits();
            bool hasTimeControl = false;

            for (int i = 1; i < parts.Length; i++)
            {
                switch (parts[i])
                {
                    case "depth":
                        if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int depth))
                        {
                            limits.Depth = Math.Max(1, Math.Min(100, depth)); // Clamp depth
                            hasTimeControl = true;
                            i++;
                        }
                        break;

                    case "movetime":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long moveTime))
                        {
                            limits.MoveTime = Math.Max(10, moveTime); // Minimum 10ms
                            hasTimeControl = true;
                            i++;
                        }
                        break;

                    case "wtime":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long wtime))
                        {
                            if (position.Turn == Color.White)
                            {
                                limits.Time = Math.Max(0, wtime);
                                hasTimeControl = true;
                            }
                            i++;
                        }
                        break;

                    case "btime":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long btime))
                        {
                            if (position.Turn == Color.Black)
                            {
                                limits.Time = Math.Max(0, btime);
                                hasTimeControl = true;
                            }
                            i++;
                        }
                        break;

                    case "winc":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long winc))
                        {
                            if (position.Turn == Color.White)
                            {
                                limits.Inc = Math.Max(0, winc);
                            }
                            i++;
                        }
                        break;

                    case "binc":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long binc))
                        {
                            if (position.Turn == Color.Black)
                            {
                                limits.Inc = Math.Max(0, binc);
                            }
                            i++;
                        }
                        break;

                    case "movestogo":
                        if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int mtg))
                        {
                            limits.MovesToGo = Math.Max(1, mtg);
                            i++;
                        }
                        break;

                    case "infinite":
                        limits.Infinite = true;
                        hasTimeControl = true;
                        break;
                }
            }

            // If no time control specified, set reasonable defaults
            if (!hasTimeControl)
            {
                limits.Depth = 10;
                limits.MoveTime = 5000;
            }

            return limits;
        }

        private async Task HandleStop()
        {
            if (isSearching && searchTask != null)
            {
                // Tell the search to stop
                search.StopSearch();

                // Cancel the task
                searchCancellation?.Cancel();

                try
                {
                    // Wait for the task to complete with a shorter timeout
                    await searchTask.WaitAsync(TimeSpan.FromMilliseconds(500));
                }
                catch (TimeoutException)
                {
                    Console.Error.WriteLine("Warning: Search didn't stop in time");
                    // Force output bestmove to prevent GUI hanging
                    SendCommand("bestmove 0000");
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Stop error: {ex.Message}");
                }
                finally
                {
                    // Always clean up
                    searchCancellation?.Dispose();
                    searchCancellation = null;
                    searchTask = null;
                    isSearching = false;
                }
            }
        }

        private Move.Move ParseMove(string moveStr)
        {
            if (string.IsNullOrEmpty(moveStr) || moveStr.Length < 4)
                return new Move.Move(); // Invalid move

            try
            {
                var from = ParseSquare(moveStr.Substring(0, 2));
                var to = ParseSquare(moveStr.Substring(2, 2));

                if (from == Square.NoSquare || to == Square.NoSquare)
                    return new Move.Move(); // Invalid move

                // Generate all legal moves to find the correct move with proper flags
                var moves = new Move.Move[256];
                var moveCount = GenerateMovesForPosition(moves);

                // Find the matching move
                for (int i = 0; i < moveCount; i++)
                {
                    if (moves[i].From == from && moves[i].To == to)
                    {
                        // For promotions, check if the promotion piece matches
                        if (moveStr.Length == 5)
                        {
                            var promoPiece = char.ToLower(moveStr[4]);
                            var expectedFlags = GetPromotionFlags(promoPiece, position.At(to) != Piece.NoPiece);
                            if (moves[i].Flags == expectedFlags)
                            {
                                return moves[i];
                            }
                        }
                        else
                        {
                            // Return first matching non-promotion move
                            if ((moves[i].Flags & MoveFlags.Promotions) == 0)
                            {
                                return moves[i];
                            }
                        }
                    }
                }

                // If we get here, the move wasn't found in legal moves
                Console.Error.WriteLine($"Move {moveStr} not found in legal moves for position {position.Fen()}");
                return new Move.Move();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Move parsing error for '{moveStr}': {ex.Message}");
                return new Move.Move();
            }
        }

        private MoveFlags GetPromotionFlags(char promoPiece, bool isCapture)
        {
            return promoPiece switch
            {
                'q' => isCapture ? MoveFlags.PcQueen : MoveFlags.PrQueen,
                'r' => isCapture ? MoveFlags.PcRook : MoveFlags.PrRook,
                'b' => isCapture ? MoveFlags.PcBishop : MoveFlags.PrBishop,
                'n' => isCapture ? MoveFlags.PcKnight : MoveFlags.PrKnight,
                _ => MoveFlags.Quiet
            };
        }

        private unsafe int GenerateMovesForPosition(Move.Move[] moveBuffer)
        {
            try
            {
                fixed (Move.Move* movesPtr = moveBuffer)
                {
                    if (position.Turn == Color.White)
                        return position.GenerateLegalsInto<White>(movesPtr);
                    else
                        return position.GenerateLegalsInto<Black>(movesPtr);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Move generation error: {ex.Message}");
                return 0;
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
            try
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
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Perft error: {ex.Message}");
            }
        }

        private void HandleBench()
        {
            try
            {
                Console.WriteLine("Running benchmark suite...");
                Console.WriteLine();
                Perft.RunBenchmark();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Benchmark error: {ex.Message}");
            }
        }
    }
}