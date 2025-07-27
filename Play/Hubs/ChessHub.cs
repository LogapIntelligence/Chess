using Microsoft.AspNetCore.SignalR;
using Play.Services;

namespace Play.Hubs
{
    public class ChessHub : Hub
    {
        private readonly IUciEngineService _engineService;
        private readonly ILogger<ChessHub> _logger;

        public ChessHub(IUciEngineService engineService, ILogger<ChessHub> logger)
        {
            _engineService = engineService;
            _logger = logger;
        }

        public async Task SendCommand(string command)
        {
            try
            {
                await _engineService.SendCommandAsync(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending command: {command}");
                await Clients.Caller.SendAsync("Error", $"Failed to send command: {ex.Message}");
            }
        }

        public async Task NewGame()
        {
            try
            {
                await _engineService.NewGameAsync();
                await Clients.All.SendAsync("GameReset");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting new game");
                await Clients.Caller.SendAsync("Error", $"Failed to start new game: {ex.Message}");
            }
        }

        public async Task MakeMove(string move)
        {
            try
            {
                await _engineService.MakeMoveAsync(move);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error making move: {move}");
                await Clients.Caller.SendAsync("Error", $"Failed to make move: {ex.Message}");
            }
        }

        public async Task SetPosition(string fen)
        {
            try
            {
                await _engineService.SetPositionAsync(fen);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting position: {fen}");
                await Clients.Caller.SendAsync("Error", $"Failed to set position: {ex.Message}");
            }
        }

        public async Task StartAnalysis()
        {
            try
            {
                await _engineService.StartAnalysisAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting analysis");
                await Clients.Caller.SendAsync("Error", $"Failed to start analysis: {ex.Message}");
            }
        }

        public async Task StopAnalysis()
        {
            try
            {
                await _engineService.StopAnalysisAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping analysis");
                await Clients.Caller.SendAsync("Error", $"Failed to stop analysis: {ex.Message}");
            }
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", Context.ConnectionId);

            // Send engine status
            var engineStatus = _engineService.IsEngineLoaded ? "loaded" : "not loaded";
            await Clients.Caller.SendAsync("EngineStatus", engineStatus);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                _logger.LogError(exception, "Client disconnected with error");
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}