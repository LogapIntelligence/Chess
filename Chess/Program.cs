namespace Chess;

using System;
using System.Diagnostics;

class Program
{
    static void Main(string[] args)
    {
        // Initialize magic bitboards and other static data
        Console.WriteLine("Initializing chess engine...");

        // Run UCI protocol
        var uci = new Uci();
        uci.Run();
    }
}