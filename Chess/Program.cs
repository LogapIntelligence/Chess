namespace Chess;

using System;
using System.Diagnostics;

class Program
{
    static void Main(string[] args)
    {
        //NNUEAnalyzer.AnalyzeNNUEFile(@"C:\Users\logap\source\repos\Chess\NNUE\models\test_nnue_final.nnue");
        // Initialize magic bitboards and other static data
        Console.WriteLine("Initializing chess engine...");

        // Run UCI protocol
        var uci = new Uci();
        uci.Run();
    }
}