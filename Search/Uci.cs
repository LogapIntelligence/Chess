using System;
using System.Threading;
using System.Threading.Tasks;
using Move;

namespace Search
{
    public class UCI
    {
        private readonly Search search;
        private Position position;
        private Task? searchTask;
        private CancellationTokenSource? searchCancellation;

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
                        Console.WriteLine("readyok");
                        break;

                    case "ucinewgame":
                        HandleNewGame();
                        break;

                    case "position":
                        HandlePosition(parts);
                        break;

                    case "go":
                        await HandleGo(parts);
                        break;

                    case "stop":
                        await HandleStop();  // Make it async
                        break;

                    case "quit":
                        await HandleStop();  // Make it async
                        return;

                    case "d":
                    case "display":
                        Console.WriteLine(position);
                        break;

                    case "perft":
                        if (parts.Length > 1 && int.TryParse(parts[1], out int depth))
                            HandlePerft(depth);
                        break;
                }
            }
        }

        private void HandleUci()
        {
            Console.WriteLine("id name CE3");
            Console.WriteLine("id author Assistant");
            Console.WriteLine("option name Hash type spin default 128 min 1 max 16384");
            Console.WriteLine("option name Threads type spin default 1 min 1 max 1");
            Console.WriteLine("uciok");
        }

        private void HandleNewGame()
        {
            position = new Position();
            Position.Set(Types.DEFAULT_FEN, position);
        }

        private void HandlePosition(string[] parts)
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

        private async Task HandleGo(string[] parts)
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
                            limits.Depth = depth;
                            hasTimeControl = true;
                            i++; // Skip next argument since we consumed it
                        }
                        break;

                    case "movetime":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long moveTime))
                        {
                            limits.MoveTime = moveTime;
                            hasTimeControl = true;
                            i++; // Skip next argument
                        }
                        break;

                    case "wtime":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long wtime) &&
                            position.Turn == Color.White)
                        {
                            limits.Time = wtime;
                            hasTimeControl = true;
                            i++; // Skip next argument
                        }
                        break;

                    case "btime":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long btime) &&
                            position.Turn == Color.Black)
                        {
                            limits.Time = btime;
                            hasTimeControl = true;
                            i++; // Skip next argument
                        }
                        break;

                    case "winc":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long winc) &&
                            position.Turn == Color.White)
                        {
                            limits.Inc = winc;
                            i++; // Skip next argument
                        }
                        break;

                    case "binc":
                        if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long binc) &&
                            position.Turn == Color.Black)
                        {
                            limits.Inc = binc;
                            i++; // Skip next argument
                        }
                        break;

                    case "movestogo":
                        if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int mtg))
                        {
                            limits.MovesToGo = mtg;
                            i++; // Skip next argument
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
                limits.Depth = 12;  // Reasonable depth limit
                limits.MoveTime = 5000;  // 5 seconds per move
            }

            // Stop any ongoing search
            await HandleStop();

            // Start new search
            searchCancellation = new CancellationTokenSource();
            searchTask = Task.Run(() =>
            {
                try
                {
                    var result = search.StartSearch(position, limits);
                    // Only output bestmove if not cancelled
                    if (!searchCancellation.Token.IsCancellationRequested)
                    {
                        Console.WriteLine($"bestmove {result.BestMove}");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Search was cancelled, don't output anything
                }
            }, searchCancellation.Token);

            try
            {
                await searchTask;
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, ignore
            }
        }

        private async Task HandleStop()
        {
            if (searchCancellation != null && searchTask != null)
            {
                // First tell the search to stop
                search.StopSearch();

                // Then cancel the task
                searchCancellation.Cancel();

                try
                {
                    // Wait for the task to complete with a timeout
                    await searchTask.WaitAsync(TimeSpan.FromSeconds(1));
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
                searchCancellation.Dispose();
                searchCancellation = null;
                searchTask = null;
            }
        }

        private Move.Move ParseMove(string moveStr)
        {
            if (moveStr.Length < 4)
                return new Move.Move();

            var from = ParseSquare(moveStr.Substring(0, 2));
            var to = ParseSquare(moveStr.Substring(2, 2));

            // Check for promotion
            MoveFlags flags = MoveFlags.Quiet;
            if (moveStr.Length > 4)
            {
                var promo = char.ToLower(moveStr[4]);
                flags = promo switch
                {
                    'n' => MoveFlags.PrKnight,
                    'b' => MoveFlags.PrBishop,
                    'r' => MoveFlags.PrRook,
                    'q' => MoveFlags.PrQueen,
                    _ => MoveFlags.Quiet
                };
            }

            // Generate legal moves to find the correct move type
            var moves = position.Turn == Color.White ?
                position.GenerateLegals<White>() :
                position.GenerateLegals<Black>();

            foreach (var move in moves)
            {
                if (move.From == from && move.To == to)
                {
                    if (flags != MoveFlags.Quiet && (move.Flags & MoveFlags.Promotions) != 0)
                    {
                        // Promotion move with specific piece
                        if ((flags == MoveFlags.PrKnight && (move.Flags == MoveFlags.PrKnight || move.Flags == MoveFlags.PcKnight)) ||
                            (flags == MoveFlags.PrBishop && (move.Flags == MoveFlags.PrBishop || move.Flags == MoveFlags.PcBishop)) ||
                            (flags == MoveFlags.PrRook && (move.Flags == MoveFlags.PrRook || move.Flags == MoveFlags.PcRook)) ||
                            (flags == MoveFlags.PrQueen && (move.Flags == MoveFlags.PrQueen || move.Flags == MoveFlags.PcQueen)))
                        {
                            return move;
                        }
                    }
                    else
                    {
                        return move;
                    }
                }
            }

            return new Move.Move(from, to, flags);
        }

        private Square ParseSquare(string sq)
        {
            if (sq.Length != 2)
                return Square.NoSquare;

            var file = (Move.File)(sq[0] - 'a');
            var rank = (Rank)(sq[1] - '1');

            return Types.CreateSquare(file, rank);
        }

        private void HandlePerft(int depth)
        {
            var startTime = DateTime.Now;
            var nodes = Perft(position, depth);
            var elapsed = (DateTime.Now - startTime).TotalSeconds;

            Console.WriteLine($"Nodes: {nodes}");
            Console.WriteLine($"Time: {elapsed:F2}s");
            Console.WriteLine($"NPS: {(int)(nodes / elapsed)}");
        }

        private ulong Perft(Position pos, int depth)
        {
            if (depth == 0)
                return 1;

            ulong nodes = 0;
            var moves = pos.Turn == Color.White ?
                pos.GenerateLegals<White>() :
                pos.GenerateLegals<Black>();

            foreach (var move in moves)
            {
                var colorToMove = pos.Turn;
                pos.Play(colorToMove, move);
                nodes += Perft(pos, depth - 1);
                pos.Undo(colorToMove, move);
            }

            return nodes;
        }
    }
}