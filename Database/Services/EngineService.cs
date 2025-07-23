using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Database.Models;
using Microsoft.Extensions.Logging;

namespace Database.Services
{
    public class EngineService : IEngineService
    {
        private readonly ILogger<EngineService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public EngineService(ILogger<EngineService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task<IEngineInstance> CreateEngineInstanceAsync(string enginePath, string parametersJson = null)
        {
            var instance = new EngineInstance(enginePath, _logger);
            await instance.InitializeAsync(parametersJson);
            return instance;
        }

        public Task<IBatchProcessor> CreateBatchProcessorAsync(Batch batch)
        {
            var processor = new BatchProcessor(batch, this, _serviceProvider, _logger);
            return Task.FromResult<IBatchProcessor>(processor);
        }
    }

    public class EngineInstance : IEngineInstance
    {
        private Process _process;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _disposed;

        public string EnginePath { get; }
        public bool IsReady { get; private set; }

        public EngineInstance(string enginePath, ILogger logger)
        {
            EnginePath = enginePath;
            _logger = logger;
        }

        public async Task InitializeAsync(string parametersJson = null)
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = EnginePath,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            _process.Start();

            // Send UCI command and wait for readyok
            await SendCommandAsync("uci");
            await WaitForResponseAsync("uciok", TimeSpan.FromSeconds(5));

            // Parse and apply engine parameters
            if (!string.IsNullOrEmpty(parametersJson))
            {
                try
                {
                    var options = JsonSerializer.Deserialize<EngineParameters>(parametersJson);

                    if (options != null)
                    {
                        // Set threads
                        if (options.Threads > 0)
                        {
                            await SendCommandAsync($"setoption name Threads value {options.Threads}");
                            _logger.LogInformation($"Set Threads to {options.Threads}");
                        }

                        // Set hash size
                        if (options.Hash > 0)
                        {
                            await SendCommandAsync($"setoption name Hash value {options.Hash}");
                            _logger.LogInformation($"Set Hash to {options.Hash} MB");
                        }

                        // Set MultiPV
                        if (options.MultiPV > 1)
                        {
                            await SendCommandAsync($"setoption name MultiPV value {options.MultiPV}");
                            _logger.LogInformation($"Set MultiPV to {options.MultiPV}");
                        }

                        // Set Contempt
                        await SendCommandAsync($"setoption name Contempt value {options.Contempt}");
                        _logger.LogInformation($"Set Contempt to {options.Contempt}");

                        // Set NNUE
                        await SendCommandAsync($"setoption name UseNNUE value {options.UseNNUE.ToString().ToLower()}");
                        _logger.LogInformation($"Set UseNNUE to {options.UseNNUE}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to parse engine parameters: {ex.Message}. Using defaults.");
                    // Fall back to defaults
                    await SendCommandAsync("setoption name Hash value 128");
                    await SendCommandAsync("setoption name Threads value 1");
                }
            }
            else
            {
                // Default options if no parameters provided
                await SendCommandAsync("setoption name Hash value 128");
                await SendCommandAsync("setoption name Threads value 1");
            }

            // Send isready and wait for readyok
            await SendCommandAsync("isready");
            await WaitForResponseAsync("readyok", TimeSpan.FromSeconds(5));

            IsReady = true;
            _logger.LogInformation($"Engine initialized: {Path.GetFileName(EnginePath)}");
        }

        public async Task<string> GetBestMoveAsync(string fen, long movetimeMs)
        {
            if (!IsReady) throw new InvalidOperationException("Engine not ready");

            await _semaphore.WaitAsync();
            try
            {
                await SendCommandAsync($"position fen {fen}");
                await SendCommandAsync($"go movetime {movetimeMs}");

                var bestMove = await WaitForBestMoveAsync(TimeSpan.FromMilliseconds(movetimeMs + 5000)); // Add buffer
                return bestMove;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<EngineAnalysis> AnalyzePositionAsync(string fen, long movetimeMs)
        {
            if (!IsReady) throw new InvalidOperationException("Engine not ready");

            await _semaphore.WaitAsync();
            try
            {
                var analysis = new EngineAnalysis();

                // Determine whose turn it is from the FEN
                var fenParts = fen.Split(' ');
                bool isBlackToMove = fenParts.Length > 1 && fenParts[1] == "b";

                await SendCommandAsync($"position fen {fen}");
                await SendCommandAsync($"go movetime {movetimeMs}");

                string line;
                var timeout = DateTime.UtcNow.AddMilliseconds(movetimeMs + 5000); // Add buffer

                while (DateTime.UtcNow < timeout)
                {
                    line = await ReadLineAsync();
                    if (line == null) break;

                    if (line.StartsWith("info depth"))
                    {
                        // Parse info line for evaluation and principal variation
                        var parts = line.Split(' ');
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i] == "depth" && i + 1 < parts.Length)
                            {
                                if (int.TryParse(parts[i + 1], out int d))
                                    analysis.Depth = d;
                            }
                            else if (parts[i] == "cp" && i + 1 < parts.Length)
                            {
                                if (int.TryParse(parts[i + 1], out int cp))
                                {
                                    // Store evaluation from White's perspective
                                    // Engine reports from side-to-move perspective, so negate for Black
                                    analysis.Evaluation = cp / 100f;
                                    if (isBlackToMove)
                                        analysis.Evaluation = -analysis.Evaluation;
                                }
                            }
                            else if (parts[i] == "mate" && i + 1 < parts.Length)
                            {
                                if (int.TryParse(parts[i + 1], out int mate))
                                {
                                    // Mate scores: positive = side to move has mate in N moves
                                    // Store as high value minus moves to prefer quicker mates
                                    // Negative = being mated in N moves
                                    if (mate > 0)
                                        analysis.Evaluation = 10000 - mate;  // e.g., mate in 3 = 9997
                                    else
                                        analysis.Evaluation = -10000 - mate; // e.g., mated in 3 = -9997

                                    // Convert to White's perspective
                                    if (isBlackToMove)
                                        analysis.Evaluation = -analysis.Evaluation;
                                }
                            }
                            else if (parts[i] == "nodes" && i + 1 < parts.Length)
                            {
                                if (long.TryParse(parts[i + 1], out long nodes))
                                    analysis.Nodes = nodes;
                            }
                            else if (parts[i] == "pv" && i + 1 < parts.Length)
                            {
                                var pvMoves = new List<string>();
                                for (int j = i + 1; j < parts.Length; j++)
                                    pvMoves.Add(parts[j]);
                                analysis.PrincipalVariation = string.Join(" ", pvMoves);
                            }
                        }
                    }
                    else if (line.StartsWith("bestmove"))
                    {
                        var parts = line.Split(' ');
                        if (parts.Length > 1)
                        {
                            analysis.BestMove = parts[1];
                        }
                        break;
                    }
                }

                return analysis;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task SendCommandAsync(string command)
        {
            if (_process?.StandardInput == null) return;
            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync();
            _logger.LogDebug($"Sent to engine: {command}");
        }

        private async Task<string> ReadLineAsync()
        {
            if (_process?.StandardOutput == null) return null;
            var line = await _process.StandardOutput.ReadLineAsync();
            if (line != null)
                _logger.LogDebug($"Received from engine: {line}");
            return line;
        }

        private async Task WaitForResponseAsync(string expectedResponse, TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < endTime)
            {
                var line = await ReadLineAsync();
                if (line?.Contains(expectedResponse) == true)
                    return;
            }
            throw new TimeoutException($"Timeout waiting for '{expectedResponse}'");
        }

        private async Task<string> WaitForBestMoveAsync(TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < endTime)
            {
                var line = await ReadLineAsync();
                if (line?.StartsWith("bestmove") == true)
                {
                    var parts = line.Split(' ');
                    return parts.Length > 1 ? parts[1] : null;
                }
            }
            throw new TimeoutException("Timeout waiting for bestmove");
        }

        public async Task QuitAsync()
        {
            if (_process != null && !_process.HasExited)
            {
                await SendCommandAsync("quit");
                if (!_process.WaitForExit(5000))
                {
                    _process.Kill();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            IsReady = false;

            try
            {
                QuitAsync().Wait(5000);
            }
            catch { }

            _process?.Dispose();
            _semaphore?.Dispose();
        }
    }

    // Helper class for deserializing engine parameters
    public class EngineParameters
    {
        public int Threads { get; set; }
        public int Hash { get; set; }
        public int MultiPV { get; set; }
        public bool UseNNUE { get; set; }
        public int Contempt { get; set; }
    }
}