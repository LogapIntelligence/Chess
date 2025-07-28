using Play.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Text;

namespace Play.Services
{
    public interface IUciEngineService
    {
        Task<bool> LoadEngineAsync(string enginePath);
        Task UnloadEngineAsync();
        Task SendCommandAsync(string command);
        Task NewGameAsync();
        bool IsEngineLoaded { get; }
    }

    public class UciEngineService : IUciEngineService, IDisposable
    {
        private Process? _engineProcess;
        private readonly IHubContext<ChessHub> _hubContext;
        private readonly ILogger<UciEngineService> _logger;
        private bool _isEngineReady = false;
        private readonly StringBuilder _outputBuffer = new();

        public bool IsEngineLoaded => _engineProcess != null && !_engineProcess.HasExited && _isEngineReady;

        public UciEngineService(IHubContext<ChessHub> hubContext, ILogger<UciEngineService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<bool> LoadEngineAsync(string enginePath)
        {
            try
            {
                if (!File.Exists(enginePath))
                {
                    _logger.LogError($"Engine file not found: {enginePath}");
                    return false;
                }

                await UnloadEngineAsync();

                _engineProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = enginePath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                _engineProcess.OutputDataReceived += OnEngineOutput;
                _engineProcess.ErrorDataReceived += OnEngineError;

                _engineProcess.Start();
                _engineProcess.BeginOutputReadLine();
                _engineProcess.BeginErrorReadLine();

                // Initialize UCI protocol
                await SendCommandAsync("uci");

                // Wait for uciok response (with timeout)
                var timeout = DateTime.Now.AddSeconds(10);
                while (!_isEngineReady && DateTime.Now < timeout)
                {
                    await Task.Delay(100);
                }

                if (_isEngineReady)
                {
                    await SendCommandAsync("ucinewgame");
                    // Use FEN for starting position
                    await SendCommandAsync("position fen rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                    await _hubContext.Clients.All.SendAsync("EngineLoaded", "Engine loaded successfully");
                }

                return _isEngineReady;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load engine");
                return false;
            }
        }

        public async Task UnloadEngineAsync()
        {
            if (_engineProcess != null)
            {
                try
                {
                    await SendCommandAsync("quit");
                    await Task.Delay(1000); // Give engine time to quit gracefully

                    if (!_engineProcess.HasExited)
                    {
                        _engineProcess.Kill();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while unloading engine");
                }
                finally
                {
                    _engineProcess?.Dispose();
                    _engineProcess = null;
                    _isEngineReady = false;
                    await _hubContext.Clients.All.SendAsync("EngineUnloaded", "Engine unloaded");
                }
            }
        }

        public async Task SendCommandAsync(string command)
        {
            if (_engineProcess?.StandardInput != null && !_engineProcess.HasExited)
            {
                await _engineProcess.StandardInput.WriteLineAsync(command);
                await _engineProcess.StandardInput.FlushAsync();
                _logger.LogInformation($"Sent to engine: {command}");
            }
        }

        public async Task NewGameAsync()
        {
            if (IsEngineLoaded)
            {
                await SendCommandAsync("ucinewgame");
                // Use FEN for starting position
                await SendCommandAsync("position fen rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            }
        }

        private void OnEngineOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            _logger.LogInformation($"Engine output: {e.Data}");

            // Handle UCI protocol responses
            if (e.Data == "uciok")
            {
                _isEngineReady = true;
            }
            else if (e.Data.StartsWith("info"))
            {
                ParseEngineInfo(e.Data);
            }
            else if (e.Data.StartsWith("bestmove"))
            {
                ParseBestMove(e.Data);
            }

            // Send all engine output to clients
            _hubContext.Clients.All.SendAsync("EngineOutput", e.Data);
        }

        private void OnEngineError(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogError($"Engine error: {e.Data}");
                _hubContext.Clients.All.SendAsync("EngineError", e.Data);
            }
        }

        private void ParseEngineInfo(string info)
        {
            // Parse UCI info string for evaluation and principal variation
            var parts = info.Split(' ');
            var evaluation = "";
            var pv = "";
            var depth = "";
            var nodes = "";

            for (int i = 0; i < parts.Length; i++)
            {
                switch (parts[i])
                {
                    case "depth":
                        if (i + 1 < parts.Length) depth = parts[i + 1];
                        break;
                    case "score":
                        if (i + 1 < parts.Length && parts[i + 1] == "cp" && i + 2 < parts.Length)
                        {
                            evaluation = (int.Parse(parts[i + 2]) / 100.0).ToString("F2");
                        }
                        else if (i + 1 < parts.Length && parts[i + 1] == "mate" && i + 2 < parts.Length)
                        {
                            evaluation = $"#{parts[i + 2]}";
                        }
                        break;
                    case "nodes":
                        if (i + 1 < parts.Length) nodes = parts[i + 1];
                        break;
                    case "pv":
                        pv = string.Join(" ", parts.Skip(i + 1));
                        break;
                }
            }

            if (!string.IsNullOrEmpty(evaluation))
            {
                _hubContext.Clients.All.SendAsync("EngineEvaluation", new
                {
                    evaluation,
                    depth,
                    nodes,
                    principalVariation = pv
                });
            }
        }

        private void ParseBestMove(string bestMoveString)
        {
            var parts = bestMoveString.Split(' ');
            if (parts.Length >= 2)
            {
                var bestMove = parts[1];
                _hubContext.Clients.All.SendAsync("EngineBestMove", bestMove);
            }
        }

        public void Dispose()
        {
            UnloadEngineAsync().Wait();
        }
    }
}