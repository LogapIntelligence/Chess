using Database.Context;
using Database.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Database.Hubs
{
    public class DashboardHub : Hub
    {
        private readonly IServiceProvider _serviceProvider;

        public DashboardHub(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public override async Task OnConnectedAsync()
        {
            // Send current status to the newly connected client
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MainContext>();
            var batchQueueService = scope.ServiceProvider.GetRequiredService<IBatchQueueService>();

            // Get overall stats
            var stats = new
            {
                totalGames = await context.ChessGames.CountAsync(),
                totalMoves = await context.ChessMoves.CountAsync(),
                activeGenerations = batchQueueService.GetActiveProcessorCount(),
                queueLength = batchQueueService.GetQueueLength()
            };

            await Clients.Caller.SendAsync("UpdateDashboardStats", stats);

            // Get active batches with their current state
            var activeBatches = await context.Batches
                .Include(b => b.Engine)
                .Include(b => b.Games)
                .Where(b => b.Status == "InProgress")
                .Select(b => new
                {
                    batchId = b.Id,
                    batchUuid = b.BatchId,
                    engineName = b.Engine.Name,
                    movetime = b.MovetimeMs,
                    totalGames = b.TotalGames,
                    completedGames = b.Games.Count(),
                    status = b.Status
                })
                .ToListAsync();

            await Clients.Caller.SendAsync("InitializeActiveBatches", activeBatches);

            await base.OnConnectedAsync();
        }

        // Method to broadcast stats updates
        public async Task BroadcastStatsUpdate(object stats)
        {
            await Clients.All.SendAsync("UpdateDashboardStats", stats);
        }

        // Method to broadcast batch updates
        public async Task BroadcastBatchUpdate(object batchInfo)
        {
            await Clients.All.SendAsync("UpdateBatch", batchInfo);
        }

        // Method to broadcast new move in a game
        public async Task BroadcastMove(object moveData)
        {
            await Clients.All.SendAsync("NewMove", moveData);
        }

        // Method to broadcast game completion
        public async Task BroadcastGameComplete(object gameData)
        {
            await Clients.All.SendAsync("GameComplete", gameData);
        }
    }
}
