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