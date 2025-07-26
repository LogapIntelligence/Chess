namespace Chess;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// UCI (Universal Chess Interface) implementation for the chess engine.
/// UCI is a standard protocol that allows chess engines to communicate with chess GUIs.
/// This class handles all UCI commands and manages the chess engine's state.
/// </summary>
public class Uci
{
    // Core components of the chess engine
    private Board _board;                    // The current chess board position
    private Search _search;                  // The search algorithm for finding best moves
    private PositionHistory _positionHistory; // Tracks position history for repetition detection

    // Search management
    private CancellationTokenSource? _searchCts; // Used to cancel ongoing searches
    private Task? _searchTask;                   // The current search task running asynchronously

    // UCI options
    private bool _ponder = false;    // Whether the engine should think during opponent's turn
    private int _multiPv = 1;        // Number of principal variations to calculate (for analysis)
    private bool _debug = false;     // Whether to output debug information

    // Engine identification constants
    private const string EngineName = "Chess12";
    private const string Author = "Angelo Wolff";

    /// <summary>
    /// Initializes the UCI interface with starting position
    /// </summary>
    public Uci()
    {
        _board = Board.StartingPosition();      // Standard chess starting position
        _search = new Search(128);              // Initialize search with 128MB hash table
        _positionHistory = new PositionHistory(_board); // Start tracking positions
    }

    /// <summary>
    /// Handles the "ucinewgame" command - prepares engine for a new game
    /// Clears hash tables and resets to starting position
    /// </summary>
    private void HandleNewGame()
    {
        // Cancel any ongoing search before starting new game
        HandleStop();

        _board = Board.StartingPosition();
        _search.ClearHash();  // Clear transposition table for fresh analysis
        _positionHistory = new PositionHistory(_board);
    }

    /// <summary>
    /// Main UCI loop - reads commands from stdin and processes them
    /// This is the entry point for the UCI interface
    /// </summary>
    public void Run()
    {
        // Don't output anything on startup - wait for UCI command per protocol
        string? input;

        // Main command loop - read until EOF or "quit" command
        while ((input = Console.ReadLine()) != null)
        {
            // Debug mode logs all received commands
            if (_debug)
                SendOutput($"info string received: {input}");

            // Split command into tokens for parsing
            string[] tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue; // Skip empty lines

            try
            {
                // Parse and handle UCI commands
                switch (tokens[0])
                {
                    case "uci":
                        // GUI wants to know engine capabilities
                        HandleUci();
                        break;

                    case "debug":
                        // Enable/disable debug mode
                        if (tokens.Length > 1)
                            _debug = tokens[1] == "on";
                        break;

                    case "isready":
                        // GUI checking if engine is responsive
                        HandleIsReady();
                        break;

                    case "ucinewgame":
                        // GUI indicates a new game is starting
                        HandleNewGame();
                        break;

                    case "position":
                        // Set up a specific board position
                        HandlePosition(tokens);
                        break;

                    case "go":
                        // Start calculating best move
                        HandleGo(tokens);
                        break;

                    case "stop":
                        // Stop current calculation
                        HandleStop();
                        break;

                    case "quit":
                        // Exit the engine
                        HandleStop();
                        return;

                    case "d":
                    case "display":
                        // Non-standard command to display board (for debugging)
                        DisplayBoard();
                        break;

                    case "perft":
                        // Performance test command
                        if (tokens.Length > 1 && int.TryParse(tokens[1], out int depth))
                            Perft(depth);
                        break;

                    case "setoption":
                        // Configure engine options
                        HandleSetOption(tokens);
                        break;

                    case "ponderhit":
                        // Opponent played the expected move during pondering
                        HandlePonderHit();
                        break;

                    default:
                        // Unknown command - log if in debug mode
                        if (_debug)
                            SendOutput($"info string unknown command: {tokens[0]}");
                        break;
                }
            }
            catch (Exception ex)
            {
                // Log errors in debug mode but don't crash
                if (_debug)
                    SendOutput($"info string error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Sends output to the GUI with proper flushing
    /// Critical: Must flush output for GUI communication to work properly
    /// </summary>
    private void SendOutput(string message)
    {
        Console.WriteLine(message);
        Console.Out.Flush(); // Critical for GUI communication!
    }

    /// <summary>
    /// Handles "uci" command - identifies engine and lists supported options
    /// </summary>
    private void HandleUci()
    {
        // Identify the engine
        SendOutput($"id name {EngineName}");
        SendOutput($"id author {Author}");

        // List all supported UCI options with their types and constraints
        SendOutput("option name Hash type spin default 128 min 1 max 16384");      // Hash table size in MB
        SendOutput("option name Threads type spin default 1 min 1 max 1");         // Number of search threads (currently single-threaded)
        SendOutput("option name Ponder type check default false");                 // Think during opponent's turn
        SendOutput("option name MultiPV type spin default 1 min 1 max 500");       // Multiple principal variations
        SendOutput("option name Debug type check default false");                  // Debug logging

        // Signal that UCI initialization is complete
        SendOutput("uciok");
    }

    /// <summary>
    /// Handles "isready" command - ensures engine is ready for next command
    /// Must wait for any ongoing operations to complete
    /// </summary>
    private void HandleIsReady()
    {
        // Make sure any ongoing search is properly handled
        WaitForSearchToFinish();
        SendOutput("readyok");
    }

    /// <summary>
    /// Handles "setoption" command - configures engine parameters
    /// Format: setoption name [option_name] value [option_value]
    /// </summary>
    private void HandleSetOption(string[] tokens)
    {
        // Don't allow option changes during search
        if (_searchTask != null && !_searchTask.IsCompleted)
        {
            if (_debug)
                SendOutput("info string cannot change options during search");
            return;
        }

        // Validate command format
        if (tokens.Length < 5 || tokens[1] != "name" || tokens[3] != "value")
            return;

        string optionName = tokens[2].ToLower();
        // Join remaining tokens in case value contains spaces
        string value = string.Join(" ", tokens.Skip(4));

        // Process each option
        switch (optionName)
        {
            case "hash":
                // Set transposition table size
                if (int.TryParse(value, out int hashSize) && hashSize > 0)
                {
                    _search = new Search(hashSize); // Create new search with specified hash size
                    if (_debug)
                        SendOutput($"info string hash size set to {hashSize} MB");
                }
                break;

            case "ponder":
                // Enable/disable pondering (thinking on opponent's time)
                _ponder = value.ToLower() == "true";
                break;

            case "multipv":
                // Set number of principal variations to calculate
                if (int.TryParse(value, out int multiPv) && multiPv > 0)
                    _multiPv = Math.Min(multiPv, 500); // Cap at 500 to prevent excessive computation
                break;

            case "debug":
                // Enable/disable debug output
                _debug = value.ToLower() == "true";
                break;
        }
    }

    /// <summary>
    /// Handles "position" command - sets up the board position
    /// Format: position [startpos | fen <fenstring>] [moves <move1> <move2> ...]
    /// </summary>
    private void HandlePosition(string[] tokens)
    {
        int index = 1; // Start parsing after "position"

        // Check if it's startpos or FEN
        if (index < tokens.Length && tokens[index] == "startpos")
        {
            // Set up standard starting position
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
                // FEN format: pieces castling ep halfmove fullmove
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

        // Initialize position history for new position
        _positionHistory = new PositionHistory(_board);

        // Apply any moves listed after position
        if (index < tokens.Length && tokens[index] == "moves")
        {
            index++;
            // Apply each move in sequence
            for (int i = index; i < tokens.Length; i++)
            {
                if (!TryParseMove(tokens[i], out Move move))
                {
                    if (_debug)
                        SendOutput($"info string invalid move: {tokens[i]}");
                    break; // Stop on first invalid move
                }
                _board.MakeMove(move);
                _positionHistory.AddPosition(_board); // Track for repetition detection
            }
        }
    }

    /// <summary>
    /// Handles "go" command - starts the search for best move
    /// Supports various time control and search limit parameters
    /// </summary>
    private void HandleGo(string[] tokens)
    {
        // Stop any ongoing search first
        HandleStop();

        // Initialize search parameters with defaults
        long timeMs = long.MaxValue;      // Time limit in milliseconds
        int depth = Search.MaxDepth;      // Depth limit
        bool isInfinite = false;          // Search until "stop" command
        bool isPonder = false;            // Pondering mode

        // Time control parameters
        long wtime = 0, btime = 0;       // White/black time remaining (ms)
        long winc = 0, binc = 0;         // White/black increment per move (ms)
        int movestogo = 40;              // Moves until next time control

        // Parse go parameters
        for (int i = 1; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "infinite":
                    // Search until explicitly stopped
                    isInfinite = true;
                    timeMs = long.MaxValue;
                    break;

                case "depth":
                    // Limit search to specific depth
                    if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out int d))
                    {
                        depth = Math.Min(d, Search.MaxDepth);
                        i++; // Skip next token (the value)
                    }
                    break;

                case "movetime":
                    // Fixed time per move
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out long mt))
                    {
                        timeMs = mt;
                        i++;
                    }
                    break;

                case "wtime":
                    // White's remaining time
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out wtime))
                    {
                        i++;
                    }
                    break;

                case "btime":
                    // Black's remaining time
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out btime))
                    {
                        i++;
                    }
                    break;

                case "winc":
                    // White's increment
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out winc))
                    {
                        i++;
                    }
                    break;

                case "binc":
                    // Black's increment
                    if (i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out binc))
                    {
                        i++;
                    }
                    break;

                case "movestogo":
                    // Moves until next time control
                    if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out movestogo))
                    {
                        i++;
                    }
                    break;

                case "ponder":
                    // Start pondering (thinking on opponent's time)
                    isPonder = true;
                    break;
            }
        }

        // Calculate time allocation if not infinite or fixed time
        if (!isInfinite && timeMs == long.MaxValue && (wtime > 0 || btime > 0))
        {
            // Get time for current side
            long timeLeft = _board.SideToMove == Color.White ? wtime : btime;
            long increment = _board.SideToMove == Color.White ? winc : binc;

            // Simple time management algorithm
            if (movestogo > 0)
            {
                // Allocate time evenly for remaining moves
                timeMs = timeLeft / movestogo;

                // Add most of increment (keep some as buffer)
                if (increment > 0)
                    timeMs += increment * 3 / 4;
            }
            else
            {
                // Sudden death time control - use small fraction of remaining time
                timeMs = timeLeft / 20;
            }

            // Safety margin - always keep some time in reserve to avoid time loss
            if (timeLeft > 1000)
                timeMs = Math.Min(timeMs, timeLeft - 50);  // Keep 50ms buffer
            else if (timeLeft > 100)
                timeMs = Math.Min(timeMs, timeLeft - 10);  // Keep 10ms buffer for low time
        }

        if (_debug)
        {
            SendOutput($"info string starting search depth={depth} time={timeMs}ms " +
                      $"infinite={isInfinite} ponder={isPonder}");
        }

        // Start the search asynchronously (non-blocking)
        StartSearch(_board.Clone(), timeMs, depth);
    }

    /// <summary>
    /// Starts an asynchronous search on a copy of the board
    /// </summary>
    private void StartSearch(Board board, long timeMs, int depth)
    {
        // Create new cancellation token for this search
        _searchCts = new CancellationTokenSource();

        // Run search in background task
        _searchTask = Task.Run(() =>
        {
            try
            {
                // The search will handle sending info strings and bestmove
                _search.Think(ref board, timeMs, depth);
            }
            catch (OperationCanceledException e)
            {
                // Expected when search is stopped - send null move
                SendOutput("bestmove 0000");
            }
            catch (Exception ex)
            {
                // Unexpected error - log and send null move
                if (_debug)
                    SendOutput($"info string error in search: {ex.Message}");
                SendOutput("bestmove 0000");
            }
        }, _searchCts.Token);
    }

    /// <summary>
    /// Handles "stop" command - stops the current search
    /// </summary>
    private void HandleStop()
    {
        // Signal search algorithm to stop
        if (_search != null)
            _search.Stop();

        // Cancel the search task
        _searchCts?.Cancel();

        // Don't wait here - let the search finish sending bestmove
        // The search task will handle cleanup
    }

    /// <summary>
    /// Handles "ponderhit" - opponent played the expected move
    /// Converts pondering to normal search
    /// </summary>
    private void HandlePonderHit()
    {
        // For now, just convert ponder to normal search
        // TODO: Implement proper pondering time management
        _ponder = false;
    }

    /// <summary>
    /// Waits for current search to finish (with timeout)
    /// Used to ensure engine is in stable state
    /// </summary>
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

    /// <summary>
    /// Parses a move in UCI format (e.g., "e2e4", "e7e8q" for promotion)
    /// Returns true if move is legal in current position
    /// </summary>
    private bool TryParseMove(string moveStr, out Move move)
    {
        move = default;

        // UCI move format: 4-5 characters (from-square, to-square, optional promotion)
        if (moveStr.Length < 4 || moveStr.Length > 5)
            return false;

        // Parse source and destination squares
        int from = ParseSquare(moveStr.Substring(0, 2));
        int to = ParseSquare(moveStr.Substring(2, 2));

        if (from < 0 || to < 0)
            return false;

        // Generate all legal moves to find matching one
        MoveList moves = new MoveList();
        MoveGenerator.GenerateMoves(ref _board, ref moves);

        // Search for move with matching from/to squares
        for (int i = 0; i < moves.Count; i++)
        {
            Move m = moves[i];
            if (m.From == from && m.To == to)
            {
                // Check promotion piece if specified
                if (moveStr.Length == 5)
                {
                    // Parse promotion piece (5th character)
                    PieceType promo = moveStr[4] switch
                    {
                        'q' => PieceType.Queen,
                        'r' => PieceType.Rook,
                        'b' => PieceType.Bishop,
                        'n' => PieceType.Knight,
                        _ => PieceType.None
                    };

                    // Skip if promotion doesn't match
                    if (m.Promotion != promo)
                        continue;
                }
                else if (m.IsPromotion)
                {
                    // Promotion move requires promotion piece
                    continue;
                }

                move = m;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses algebraic square notation (e.g., "e4") to square index (0-63)
    /// </summary>
    private int ParseSquare(string square)
    {
        if (square.Length != 2)
            return -1;

        int file = square[0] - 'a';  // Convert 'a'-'h' to 0-7
        int rank = square[1] - '1';  // Convert '1'-'8' to 0-7

        // Validate bounds
        if (file < 0 || file > 7 || rank < 0 || rank > 7)
            return -1;

        // Convert to 0-63 index (a1=0, h8=63)
        return rank * 8 + file;
    }

    /// <summary>
    /// Displays the current board position in ASCII format
    /// Non-standard UCI command useful for debugging
    /// </summary>
    private void DisplayBoard()
    {
        var output = new System.Text.StringBuilder();

        // Column labels
        output.AppendLine("\n  a b c d e f g h");
        output.AppendLine("  ---------------");

        // Display board from white's perspective (rank 8 to 1)
        for (int rank = 7; rank >= 0; rank--)
        {
            output.Append($"{rank + 1}|"); // Row label

            for (int file = 0; file < 8; file++)
            {
                int square = rank * 8 + file;
                var (piece, color) = _board.GetPieceAt(square);

                // Convert piece to character (lowercase for black, uppercase for white)
                char pieceChar = piece switch
                {
                    PieceType.Pawn => 'p',
                    PieceType.Knight => 'n',
                    PieceType.Bishop => 'b',
                    PieceType.Rook => 'r',
                    PieceType.Queen => 'q',
                    PieceType.King => 'k',
                    _ => '.'  // Empty square
                };

                // Uppercase for white pieces
                if (color == Color.White && pieceChar != '.')
                    pieceChar = char.ToUpper(pieceChar);

                output.Append($"{pieceChar} ");
            }
            output.AppendLine($"|{rank + 1}");
        }

        output.AppendLine("  ---------------");
        output.AppendLine("  a b c d e f g h\n");

        // Display position details
        output.AppendLine($"FEN: {FenParser.ToFen(ref _board)}");
        output.AppendLine($"Side to move: {_board.SideToMove}");
        output.AppendLine($"Castling rights: {_board.CastlingRights}");

        // Format en passant square
        output.AppendLine($"En passant: {(_board.EnPassantSquare >= 0 ?
            $"{(char)('a' + _board.EnPassantSquare % 8)}{_board.EnPassantSquare / 8 + 1}" : "-")}");

        output.AppendLine($"Halfmove clock: {_board.HalfmoveClock}");
        output.AppendLine($"Fullmove number: {_board.FullmoveNumber}");

        SendOutput(output.ToString());
    }

    /// <summary>
    /// Perft (performance test) - counts leaf nodes at given depth
    /// Used to verify move generation correctness
    /// </summary>
    private void Perft(int depth)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        SendOutput($"Starting perft({depth})...");

        // Use divide for depth > 1 to show move breakdown
        if (depth > 1)
        {
            PerftDivide(depth);
        }
        else
        {
            // Simple node count for depth 1
            long nodes = PerftRecursive(ref _board, depth);
            sw.Stop();

            SendOutput($"Nodes: {nodes}");
            SendOutput($"Time: {sw.ElapsedMilliseconds} ms");
            // Calculate nodes per second
            SendOutput($"NPS: {(nodes * 1000) / Math.Max(sw.ElapsedMilliseconds, 1)}");
        }
    }

    /// <summary>
    /// Perft divide - shows node count for each root move
    /// Useful for debugging move generation issues
    /// </summary>
    private void PerftDivide(int depth)
    {
        MoveList moves = new MoveList();
        MoveGenerator.GenerateMoves(ref _board, ref moves);

        long totalNodes = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Count nodes for each root move
        for (int i = 0; i < moves.Count; i++)
        {
            Board copy = _board;  // Make copy to preserve original
            copy.MakeMove(moves[i]);

            // Special case: depth 1 just counts the move itself
            long nodes = depth == 1 ? 1 : PerftRecursive(ref copy, depth - 1);
            totalNodes += nodes;

            // Output nodes for this move
            SendOutput($"{moves[i]}: {nodes}");
        }

        sw.Stop();
        double nps = totalNodes / Math.Max(sw.Elapsed.TotalSeconds, 0.001);

        SendOutput($"\nTotal: {totalNodes} nodes");
        SendOutput($"Time: {sw.ElapsedMilliseconds}ms");
        SendOutput($"NPS: {nps:N0}");
    }

    /// <summary>
    /// Recursive perft implementation - counts all leaf nodes
    /// </summary>
    private long PerftRecursive(ref Board board, int depth)
    {
        // Base case: leaf node
        if (depth == 0)
            return 1;

        MoveList moves = new MoveList();
        MoveGenerator.GenerateLegalMoves(ref board, ref moves);

        // Optimization: at depth 1, just return move count
        if (depth == 1)
            return moves.Count;

        // Recursively count nodes for all moves
        long nodes = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            Board newBoard = board;  // Copy board
            newBoard.MakeMove(moves[i]);
            nodes += PerftRecursive(ref newBoard, depth - 1);
        }

        return nodes;
    }
}