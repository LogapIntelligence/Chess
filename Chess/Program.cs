namespace Chess;

class Program
{
    /// ============ 1 ============
    /// Main Entry UCI Standard
    /// 
    /// UCI (Universal Chess Interface) is a standard protocol for communication
    /// between chess engines and chess GUIs (Graphical User Interfaces).
    /// 
    /// The UCI protocol follows a command-response pattern where:
    /// - GUI sends commands to the engine via standard input
    /// - Engine responds via standard output
    /// 
    /// Standard UCI Commands:
    ///     -uci (initialization command)
    ///         1 -> First command sent by GUI to engine
    ///         2 -> Engine must respond with "id" information and available options
    ///         3 -> Engine must end response with "uciok"
    ///         
    ///     -isready (synchronization command)
    ///         4 -> GUI uses this to check if engine has processed all input
    ///         5 -> Engine must always respond with "readyok"
    ///         6 -> Used after position setup or before searching
    ///         
    ///     -go depth <int> (fixed depth search)
    ///         7 -> Tells engine to search to a specific depth (ply)
    ///         8 -> Engine searches position and returns best move
    ///         9 -> Example: "go depth 10" searches 10 half-moves deep
    ///         
    ///     -go infinite (infinite analysis)
    ///         10 -> Engine searches until receiving "stop" command
    ///         11 -> Used for analysis mode in GUIs
    ///         12 -> Engine sends periodic "info" updates during search
    ///         
    ///     -go movetime <int> (time-based search)
    ///         13 -> Engine searches for exactly <int> milliseconds
    ///         14 -> Example: "go movetime 5000" searches for 5 seconds
    ///         15 -> Engine automatically stops and returns best move after time expires
    ///         
    /// Additional UCI commands not shown but typically supported:
    ///     - position [fen | startpos] moves ... (set up board position)
    ///     - stop (stop calculating)
    ///     - quit (terminate engine)
    ///     - setoption name <id> value <x> (configure engine options)
    ///     - ucinewgame (prepare for new game)
    /// 
    /// <param name="args">Command line arguments (typically unused in UCI engines)</param>
    static void Main(string[] args)
    {
        // 1 -> Program execution starts here
        // 2 -> Create new UCI handler instance
        //var uci = new Uci();

        // 3 -> Start the UCI command loop
        // 4 -> This method will:
        //      a) Wait for commands from standard input
        //      b) Parse and validate UCI commands
        //      c) Execute appropriate engine functions
        //      d) Send responses to standard output
        //      e) Continue until "quit" command received
        //.Run();

        // 5 -> Program terminates after Run() returns (on "quit" command)

        Performance.RunAllBenchmarks();
    }
}