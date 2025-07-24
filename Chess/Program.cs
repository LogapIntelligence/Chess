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

        NNUEConfig.NNUEPath = @"C:\Users\logap\Downloads\nn-1ceb1ade0001.nnue";
        NNUEConfig.UseNNUE = true;  // Set to false to use classical evaluation

        // You can also set the path via command line argument
        if (args.Length > 0 && args[0] == "--nnue" && args.Length > 1)
        {
            NNUEConfig.NNUEPath = args[1];
            Console.WriteLine($"Using NNUE file: {NNUEConfig.NNUEPath}");
        }

        // Run UCI protocol
        var uci = new Uci();
        uci.Run();
    }
}