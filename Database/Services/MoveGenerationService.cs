using Database.Context;
using Database.Hubs;
using Database.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Database.Services
{
    public class MoveGenerationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MoveGenerationService> _logger;
        private readonly IHubContext<DashboardHub> _hubContext;

        public MoveGenerationService(IServiceProvider serviceProvider, ILogger<MoveGenerationService> logger, IHubContext<DashboardHub> hubContext)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Move Generation Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<MainContext>();
                    var engineService = scope.ServiceProvider.GetRequiredService<IEngineService>();

                    var pendingBatch = await context.Batches
                        .Include(b => b.Engine)
                        .FirstOrDefaultAsync(b => b.Status == "Pending", stoppingToken);

                    if (pendingBatch != null)
                    {
                        _logger.LogInformation($"Starting batch {pendingBatch.BatchId}");
                        pendingBatch.Status = "InProgress";
                        await context.SaveChangesAsync(stoppingToken);
                        await BroadcastDashboardUpdate(context);

                        try
                        {
                            await GenerateGamesForBatch(pendingBatch, engineService, context, scope.ServiceProvider, stoppingToken);
                            pendingBatch.Status = "Completed";
                            pendingBatch.CompletedAt = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error processing batch {pendingBatch.BatchId}");
                            pendingBatch.Status = "Failed";
                        }

                        await context.SaveChangesAsync(stoppingToken);
                        await BroadcastDashboardUpdate(context);
                        _logger.LogInformation($"Batch {pendingBatch.BatchId} finished.");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }

            _logger.LogInformation("Move Generation Service is stopping.");
        }

        private async Task GenerateGamesForBatch(Batch batch, IEngineService engineService, MainContext context, IServiceProvider serviceProvider, CancellationToken stoppingToken)
        {
            for (int i = 0; i < batch.TotalGames; i++)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var game = new ChessGame
                {
                    BatchId = batch.Id,
                    GeneratedAt = DateTime.UtcNow,
                    Moves = new List<ChessMove>()
                };

                var chessService = serviceProvider.GetRequiredService<IChessService>();
                chessService.NewGame();
                int moveCount = 0;

                while (!chessService.IsCheckmate() && !chessService.IsStalemate() && moveCount < (batch.Depth * 20))
                {
                    var fen = chessService.GetFen();
                    var bestMoveStr = await engineService.GetBestMoveAsync(fen, batch.Engine.FilePath, (int)batch.Depth);

                    if (string.IsNullOrEmpty(bestMoveStr) || !chessService.IsValidMove(bestMoveStr))
                    {
                        _logger.LogWarning($"Invalid move '{bestMoveStr}' for FEN '{fen}' from engine '{batch.Engine.Name}'.");
                        break;
                    }

                    chessService.ApplyMove(bestMoveStr);
                    moveCount++;

                    game.Moves.Add(new ChessMove
                    {
                        MoveNumber = moveCount,
                        Fen = chessService.GetFen(),
                        ZobristHash = 0, // Zobrist hash not implemented in custom service
                        Evaluation = 0 // Placeholder, would need engine to output eval
                    });
                }

                game.MoveCount = moveCount;
                if (chessService.IsCheckmate())
                {
                    game.Result = chessService.Turn == Player.White ? "0-1" : "1-0";
                }
                else
                {
                    game.Result = "1/2-1/2";
                }

                context.ChessGames.Add(game);
                await context.SaveChangesAsync(stoppingToken);

                // Update dashboard after each game with simple data
                await BroadcastDashboardUpdate(context);
                await BroadcastProgressUpdate(batch.Id, i + 1, batch.TotalGames);
            }
        }

        private async Task BroadcastDashboardUpdate(MainContext context)
        {
            var totalGames = await context.ChessGames.CountAsync();
            var totalMoves = await context.ChessMoves.CountAsync();
            var activeGenerations = await context.Batches.CountAsync(b => b.Status == "InProgress");

            await _hubContext.Clients.All.SendAsync("ReceiveDashboardUpdate", new { totalGames, totalMoves, activeGenerations });
        }

        private async Task BroadcastProgressUpdate(long batchId, long currentGames, long totalGames)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", new { batchId, currentGames, totalGames });
        }
    }
}