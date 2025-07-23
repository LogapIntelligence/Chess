using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace GameGenerator
{
    public class StockfishHelper : IDisposable
    {
        private Process _stockfishProcess;
        private StreamWriter _stockfishInput;
        private StreamReader _stockfishOutput;
        private readonly object _lock = new object();

        public bool Initialize()
        {
            try
            {
                // Try to find Stockfish executable
                string stockfishPath = FindStockfish();
                if (string.IsNullOrEmpty(stockfishPath))
                {
                    Console.WriteLine("Stockfish not found. Please ensure stockfish is in PATH or current directory.");
                    return false;
                }

                _stockfishProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = stockfishPath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                _stockfishProcess.Start();
                _stockfishInput = _stockfishProcess.StandardInput;
                _stockfishOutput = _stockfishProcess.StandardOutput;

                // Initialize UCI
                SendCommand("uci");
                WaitForResponse("uciok", 5000);

                // Set options for faster analysis
                SendCommand("setoption name Threads value 1");
                SendCommand("setoption name Hash value 16");
                SendCommand("isready");
                WaitForResponse("readyok", 5000);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize Stockfish: {ex.Message}");
                return false;
            }
        }

        private string FindStockfish()
        {

            return @"C:\Users\logap\Downloads\stockfish-windows-x86-64-avx2\stockfish\stockfish-windows-x86-64-avx2.exe";
        }

        public int EvaluatePosition(string fen, int depth)
        {
            lock (_lock)
            {
                try
                {
                    // Set position
                    SendCommand($"position fen {fen}");

                    // Start analysis
                    SendCommand($"go depth {depth}");

                    // Wait for bestmove and extract score
                    string bestMoveLine = WaitForResponse("bestmove", 30000);

                    // Look for score in info lines
                    int score = 0;
                    string line;
                    var scoreRegex = new Regex(@"score cp (-?\d+)");
                    var mateRegex = new Regex(@"score mate (-?\d+)");

                    // Read backwards through recent output to find score
                    var recentLines = new System.Collections.Generic.List<string>();

                    // The score should be in one of the last info lines before bestmove
                    // We'll parse the bestmove line and previous info lines
                    string[] parts = bestMoveLine.Split(new[] { "info" }, StringSplitOptions.None);

                    foreach (var part in parts)
                    {
                        var mateMatch = mateRegex.Match(part);
                        if (mateMatch.Success)
                        {
                            int mateIn = int.Parse(mateMatch.Groups[1].Value);
                            // Convert mate score to centipawns (10000 = mate)
                            score = mateIn > 0 ? 10000 - mateIn : -10000 - mateIn;
                            break;
                        }

                        var scoreMatch = scoreRegex.Match(part);
                        if (scoreMatch.Success)
                        {
                            score = int.Parse(scoreMatch.Groups[1].Value);
                            break;
                        }
                    }

                    return score;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error evaluating position: {ex.Message}");
                    return 0;
                }
            }
        }

        private void SendCommand(string command)
        {
            _stockfishInput.WriteLine(command);
            _stockfishInput.Flush();
        }

        private string WaitForResponse(string expectedResponse, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            string lastLine = "";

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (_stockfishProcess.HasExited)
                    throw new Exception("Stockfish process has exited");

                // Use Peek to check if data is available
                while (_stockfishOutput.Peek() >= 0)
                {
                    string line = _stockfishOutput.ReadLine();
                    if (line != null)
                    {
                        lastLine = line;
                        if (line.Contains(expectedResponse))
                            return line;
                    }
                }

                Thread.Sleep(10);
            }

            throw new TimeoutException($"Timeout waiting for '{expectedResponse}'");
        }

        public void Dispose()
        {
            try
            {
                if (_stockfishProcess != null && !_stockfishProcess.HasExited)
                {
                    SendCommand("quit");
                    _stockfishProcess.WaitForExit(1000);

                    if (!_stockfishProcess.HasExited)
                        _stockfishProcess.Kill();
                }

                _stockfishInput?.Dispose();
                _stockfishOutput?.Dispose();
                _stockfishProcess?.Dispose();
            }
            catch { }
        }
    }
}