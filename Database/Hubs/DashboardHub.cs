using Database.Context;
using Database.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Database.Hubs
{
    public class DashboardHub(IServiceProvider _serviceProvider) : Hub
    {
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }

        public override async Task OnConnectedAsync()
        {
            // Send current status to the newly connected client
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MainContext>();
            var batchQueueService = scope.ServiceProvider.GetRequiredService<IBatchQueueService>();

            var stats = new
            {
                totalGames = await context.ChessGames.CountAsync(),
                totalMoves = await context.ChessMoves.CountAsync(),
                activeGenerations = batchQueueService.GetActiveProcessorCount(),
                queueLength = batchQueueService.GetQueueLength()
            };

            await Clients.Caller.SendAsync("ReceiveDashboardUpdate", stats);
            await base.OnConnectedAsync();
        }
    }
}
