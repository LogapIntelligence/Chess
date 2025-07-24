namespace Chess;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class Uci
{
    private Board _board;
    private Search _search;
    private PositionHistory _positionHistory;
    private CancellationTokenSource? _searchCts;
    private Task? _searchTask;
    private bool _ponder = false;
    private int _multiPv = 1;
    private bool _debug = false;

    private const string EngineName = "Chess12";
    private const string Author = "Angelo Wolff";

    public Uci()
    {
        _board = Board.StartingPosition();
        _search = new Search(128);
        _positionHistory = new PositionHistory(_board);
    }

    private void HandleNewGame()
    {
        // Cancel any ongoing search
        HandleStop();

        _board = Board.StartingPosition();
        _search.ClearHash();
        _positionHistory = new PositionHistory(_board);
    }

    public void Run()
    {
        // Don't output anything on startup - wait for UCI command
        string? input;
        while ((input = Console.ReadLine()) != null)
        {
            if (_debug)
                SendOutput($"info string received: {input}");

            string[] tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            try
            {
                switch (tokens[0])
                {
                    case "uci":
                        HandleUci();
                        break;
                    case "debug":
                        if (tokens.Length > 1)
                            _debug = tokens[1] == "on";
                        break;
                    case "isready":
                        HandleIsReady();
                        break;
                    case "ucinewgame":
                        HandleNewGame();
                        break;
                    case "position":
                        HandlePosition(tokens);
                        break;
                    case "go":
                        HandleGo(tokens);
                        break;
                    case "stop":
                        HandleStop();
                        break;
                    case "quit":
                        HandleStop();
                        return;
                    case "d":
                    case "display":
                        DisplayBoard();
                        break;
                    case "eval":
                        DisplayEval();
                        break;
                    case "perft":
                        if (tokens.Length > 1 && int.TryParse(tokens[1], out int depth))
                            Perft(depth);
                        break;
                    case "setoption":
                        HandleSetOption(tokens);
                        break;
                    case "ponderhit":
                        HandlePonderHit();
                        break;
                    default:
                        if (_debug)
                            SendOutput($"info string unknown command: {tokens[0]}");
                        break;
                }
            }
            catch (Exception ex)
            {
                if (_debug)
                    SendOutput($"info string error: {ex.Message}");
            }
        }
    }
    private void DisplayEval()
    {
        int eval;

        // Check if NNUE is available and enabled
        if (NNUEConfig.UseNNUE && !string.IsNullOrEmpty(NNUEConfig.NNUEPath))
        {
            // Try flexible loader first
            if (FlexibleNNUELoader.TryLoadNNUE(NNUEConfig.NNUEPath, out var weights))
            {
                var nnueEvaluator = new SimpleNNUEEvaluator(weights);
                eval = nnueEvaluator.Evaluate(ref _board);
                SendOutput($"info string NNUE evaluation: {eval} cp");
                return;
            }

            // Try standard NNUE
            var standardNNUE = new NNUEEvaluator(NNUEConfig.NNUEPath);
            if (standardNNUE.IsLoaded)
            {
                eval = standardNNUE.Evaluate(ref _board);
                SendOutput($"info string NNUE evaluation: {eval} cp");
                return;
            }
        }

        // Fall back to classical evaluation
        eval = Evaluation.Evaluate(ref _board);
        SendOutput($"info string static evaluation: {eval} cp");
    }

    private void SendOutput(string message)
    {
        Console.WriteLine(message);
        Console.Out.Flush(); // Critical for GUI communication!
    }

    private void HandleUci()
    {
        SendOutput($"id name {EngineName}");
        SendOutput($"id author {Author}");
        SendOutput("option name Hash type spin default 128 min 1 max 16384");
        SendOutput("option name Threads type spin default 1 min 1 max 1");
        SendOutput("option name Ponder type check default false");
        SendOutput("option name MultiPV type spin default 1 min 1 max 500");
        SendOutput("option name Debug type check default false");
        SendOutput("uciok");
    }

    private void HandleIsReady()
    {
        // Make sure any ongoing search is properly handled
        WaitForSearchToFinish();
        SendOutput("readyok");
    }

    private void HandleSetOption(string[] tokens)
    {
        // Don't allow option changes during search
        if (_searchTask != null && !_searchTask.IsCompleted)
        {
            if (_debug)
                SendOutput("info string cannot change options during search");
            return;
        }

        if (tokens.Length < 5 || tokens[1] != "name" || tokens[3] != "value")
            return;

        string optionName = tokens[2].ToLower();
        string value = string.Join(" ", tokens.Skip(4));

        switch (optionName)
        {
            case "hash":
                if (int.TryParse(value, out int hashSize) && hashSize > 0)
                {
                    _search = new Search(hashSize);
                    if (_debug)
                        SendOutput($"info string hash size set to {hashSize} MB");
                }
                break;
            case "ponder":
                _ponder = value.ToLower() == "true";
                break;
            case "multipv":
                if (int.TryParse(value, out int multiPv) && multiPv > 0)
                    _multiPv = Math.Min(multiPv, 500);
                break;
            case "debug":
                _debug = value.ToLower() == "true";
                break;
        }
    }

    private void HandlePosition(string[] tokens)
    {
        int index = 1;

        if (index < tokens.Length && tokens[index] == "startpos")
        {
            _board = Board.StartingPosition();
            index++;
        }
        else if (index < tokens.Length && tokens[index] == "fen")
        {
            index++;
            // Reconstruct FEN string from remaining tokens until "moves"
            var fenParts = new List<string>();
            while (index < tokens.Length && tokens[index] != "moves")
            {
                fenParts.Add(tokens[index]);
                index++;
            }

            if (fenParts.Count > 0)
            {
                string fen = string.Join(" ", fenParts);
                try
                {
                    _board = FenParser.ParseFen(fen);
                }
                catch (Exception ex)
                {
                    if (_debug)
                        SendOutput($"info string error parsing FEN: {ex.Message}");
                    return;
                }
            }
        }

        _positionHistory = new PositionHistory(_board);

        if (index < tokens.Length && tokens[index] == "moves")
        {
            index++;
            for (int i = index; i < tokens.Length; i++)
            {
                if (!TryParseMove(tokens[i], out Move move))
                {
                    if (_debug)
                        SendOutput($"info string invalid move: {tokens[i]}");
                    break;
                }
                _board.MakeMove(move);
                _positionHistory.AddPosition(_board);
            }
        }
    }

    private void HandleGo(string[] tokens)
    {
        // Stop any ongoing search first
        HandleStop();

        long timeMs = long.MaxValue;
        int depth = Search.MaxDepth;
        bool isInfinite = false;
        bool isPonder = false;

        long wtime = 0, btime = 0, winc = 0, binc = 0;
        int movestogo = 40; // Default moves to go

        // Parse go parameters
        for (int i = 1; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "infinite":
                    isInfinite = true;
                    timeMs = long.MaxValue;
                    break;
                case "depth":
                    if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out int d))
                    {
                        depth = Math.Min(d, Search.MaxDepth);
                        i++;
                    }
                    break;
                case "movetime":
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out long mt))
                    {
                        timeMs = mt;
                        i++;
                    }
                    break;
                case "wtime":
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out wtime))
                    {
                        i++;
                    }
                    break;
                case "btime":
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out btime))
                    {
                        i++;
                    }
                    break;
                case "winc":
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out winc))
                    {
                        i++;
                    }
                    break;
                case "binc":
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out binc))
                    {
                        i++;
                    }
                    break;
                case "movestogo":
                    if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out movestogo))
                    {
                        i++;
                    }
                    break;
                case "ponder":
                    isPonder = true;
                    break;
            }
        }

        // Calculate time allocation if not infinite or fixed time
        if (!isInfinite && timeMs == long.MaxValue && (wtime > 0 || btime > 0))
        {
            long timeLeft = _board.SideToMove == Color.White ? wtime : btime;
            long increment = _board.SideToMove == Color.White ? winc : binc;

            // Simple time management
            if (movestogo > 0)
            {
                timeMs = timeLeft / movestogo;
                if (increment > 0)
                    timeMs += increment * 3 / 4;
            }
            else
            {
                // Sudden death - use a fraction of remaining time
                timeMs = timeLeft / 20;
            }

            // Safety margin - keep some time in reserve
            if (timeLeft > 1000)
                timeMs = Math.Min(timeMs, timeLeft - 50);
            else if (timeLeft > 100)
                timeMs = Math.Min(timeMs, timeLeft - 10);
        }

        if (_debug)
        {
            SendOutput($"info string starting search depth={depth} time={timeMs}ms " +
                      $"infinite={isInfinite} ponder={isPonder}");
        }

        // Start the search asynchronously
        StartSearch(_board.Clone(), timeMs, depth);
    }

    private void StartSearch(Board board, long timeMs, int depth)
    {
        _searchCts = new CancellationTokenSource();

        _searchTask = Task.Run(() =>
        {
            try
            {
                // The search will handle sending info and bestmove
                _search.Think(ref board, timeMs, depth);
            }
            catch (OperationCanceledException)
            {
                // Expected when search is stopped
                // Send bestmove if search didn't
                SendOutput("bestmove 0000");
            }
            catch (Exception ex)
            {
                if (_debug)
                    SendOutput($"info string error in search: {ex.Message}");
                SendOutput("bestmove 0000");
            }
        }, _searchCts.Token);
    }

    private void HandleStop()
    {
        if (_search != null)
            _search.Stop();

        _searchCts?.Cancel();

        // Don't wait here - let the search finish sending bestmove
        // The search task will handle cleanup
    }

    private void HandlePonderHit()
    {
        // For now, just convert ponder to normal search
        _ponder = false;
    }

    private void WaitForSearchToFinish()
    {
        try
        {
            _searchTask?.Wait(1000); // Wait up to 1 second
        }
        catch (AggregateException)
        {
            // Task was cancelled, which is expected
        }
        catch (OperationCanceledException)
        {
            // Task was cancelled, which is expected
        }
        _searchTask = null;
    }

    private bool TryParseMove(string moveStr, out Move move)
    {
        move = default;

        if (moveStr.Length < 4 || moveStr.Length > 5)
            return false;

        int from = ParseSquare(moveStr.Substring(0, 2));
        int to = ParseSquare(moveStr.Substring(2, 2));

        if (from < 0 || to < 0)
            return false;

        // Generate legal moves to find the matching one
        MoveList moves = new MoveList();
        MoveGenerator.GenerateMoves(ref _board, ref moves);

        for (int i = 0; i < moves.Count; i++)
        {
            Move m = moves[i];
            if (m.From == from && m.To == to)
            {
                // Check promotion
                if (moveStr.Length == 5)
                {
                    PieceType promo = moveStr[4] switch
                    {
                        'q' => PieceType.Queen,
                        'r' => PieceType.Rook,
                        'b' => PieceType.Bishop,
                        'n' => PieceType.Knight,
                        _ => PieceType.None
                    };

                    if (m.Promotion != promo)
                        continue;
                }
                else if (m.IsPromotion)
                {
                    continue; // Promotion move requires promotion piece
                }

                move = m;
                return true;
            }
        }

        return false;
    }

    private int ParseSquare(string square)
    {
        if (square.Length != 2)
            return -1;

        int file = square[0] - 'a';
        int rank = square[1] - '1';

        if (file < 0 || file > 7 || rank < 0 || rank > 7)
            return -1;

        return rank * 8 + file;
    }

    private void DisplayBoard()
    {
        var output = new System.Text.StringBuilder();
        output.AppendLine("\n  a b c d e f g h");
        output.AppendLine("  ---------------");

        for (int rank = 7; rank >= 0; rank--)
        {
            output.Append($"{rank + 1}|");
            for (int file = 0; file < 8; file++)
            {
                int square = rank * 8 + file;
                var (piece, color) = _board.GetPieceAt(square);

                char pieceChar = piece switch
                {
                    PieceType.Pawn => 'p',
                    PieceType.Knight => 'n',
                    PieceType.Bishop => 'b',
                    PieceType.Rook => 'r',
                    PieceType.Queen => 'q',
                    PieceType.King => 'k',
                    _ => '.'
                };

                if (color == Color.White && pieceChar != '.')
                    pieceChar = char.ToUpper(pieceChar);

                output.Append($"{pieceChar} ");
            }
            output.AppendLine($"|{rank + 1}");
        }

        output.AppendLine("  ---------------");
        output.AppendLine("  a b c d e f g h\n");
        output.AppendLine($"FEN: {FenParser.ToFen(ref _board)}");
        output.AppendLine($"Side to move: {_board.SideToMove}");
        output.AppendLine($"Castling rights: {_board.CastlingRights}");
        output.AppendLine($"En passant: {(_board.EnPassantSquare >= 0 ? $"{(char)('a' + _board.EnPassantSquare % 8)}{_board.EnPassantSquare / 8 + 1}" : "-")}");
        output.AppendLine($"Halfmove clock: {_board.HalfmoveClock}");
        output.AppendLine($"Fullmove number: {_board.FullmoveNumber}");

        SendOutput(output.ToString());
    }

    private void Perft(int depth)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        SendOutput($"Starting perft({depth})...");

        // Use divide for depth > 1
        if (depth > 1)
        {
            PerftDivide(depth);
        }
        else
        {
            long nodes = PerftRecursive(ref _board, depth);
            sw.Stop();

            SendOutput($"Nodes: {nodes}");
            SendOutput($"Time: {sw.ElapsedMilliseconds} ms");
            SendOutput($"NPS: {(nodes * 1000) / Math.Max(sw.ElapsedMilliseconds, 1)}");
        }
    }

    private void PerftDivide(int depth)
    {
        MoveList moves = new MoveList();
        MoveGenerator.GenerateMoves(ref _board, ref moves);

        long totalNodes = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < moves.Count; i++)
        {
            Board copy = _board;
            copy.MakeMove(moves[i]);
            long nodes = depth == 1 ? 1 : PerftRecursive(ref copy, depth - 1);
            totalNodes += nodes;
            SendOutput($"{moves[i]}: {nodes}");
        }

        sw.Stop();
        double nps = totalNodes / Math.Max(sw.Elapsed.TotalSeconds, 0.001);

        SendOutput($"\nTotal: {totalNodes} nodes");
        SendOutput($"Time: {sw.ElapsedMilliseconds}ms");
        SendOutput($"NPS: {nps:N0}");
    }

    private long PerftRecursive(ref Board board, int depth)
    {
        if (depth == 0)
            return 1;

        MoveList moves = new MoveList();
        MoveGenerator.GenerateMoves(ref board, ref moves);

        if (depth == 1)
            return moves.Count;

        long nodes = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            Board newBoard = board;
            newBoard.MakeMove(moves[i]);
            nodes += PerftRecursive(ref newBoard, depth - 1);
        }

        return nodes;
    }
}