using Database.Context;
using Database.Hubs;
using Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
                        await BroadcastActiveGamesUpdate(context);


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
                        await BroadcastActiveGamesUpdate(context);
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

                while (!chessService.IsCheckmate() && !chessService.IsStalemate() && moveCount < (batch.Depth * 20)) // Increased move limit
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
                    game.Result = chessService.Turn == Player.White ? "0-1" : "1-0"; // The player whose turn it is has been checkmated
                }
                else
                {
                    game.Result = "1/2-1/2"; // Stalemate or other draw condition
                }


                context.ChessGames.Add(game);
                await context.SaveChangesAsync(stoppingToken);

                // Update dashboard after each game
                await BroadcastDashboardUpdate(context);
                var activeBatches = await context.Batches.Where(b => b.Status == "InProgress").Include(b => b.Engine).Include(b => b.Games).ToListAsync(stoppingToken);
                await BroadcastActiveGamesUpdate(context, activeBatches);
            }
        }

        private async Task BroadcastDashboardUpdate(MainContext context)
        {
            var totalGames = await context.ChessGames.CountAsync();
            var totalMoves = await context.ChessMoves.CountAsync();
            var activeGenerations = await context.Batches.CountAsync(b => b.Status == "InProgress");

            await _hubContext.Clients.All.SendAsync("ReceiveDashboardUpdate", new { totalGames, totalMoves, activeGenerations });
        }

        private async Task BroadcastActiveGamesUpdate(MainContext context, List<Batch> activeBatches = null)
        {
            if (activeBatches == null)
            {
                activeBatches = await context.Batches
                   .Where(b => b.Status == "InProgress")
                   .Include(b => b.Engine)
                   .Include(b => b.Games)
                   .ToListAsync();
            }

            var viewHtml = await RenderPartialViewToString("Views/Home/_ActiveGamesPartial.cshtml", activeBatches);
            await _hubContext.Clients.All.SendAsync("ReceiveActiveGamesUpdate", viewHtml);
        }

        // Helper to render partial view to string
        private async Task<string> RenderPartialViewToString(string viewName, object model)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
                var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

                var viewEngine = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
                var viewResult = viewEngine.FindView(actionContext, viewName, false);

                if (viewResult.View == null)
                {
                    _logger.LogError($"Could not find view '{viewName}'");
                    return string.Empty;
                }

                using (var sw = new StringWriter())
                {
                    var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
                    var tempData = new TempDataDictionary(actionContext.HttpContext, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());

                    var viewContext = new ViewContext(
                        actionContext,
                        viewResult.View,
                        viewData,
                        tempData,
                        sw,
                        new HtmlHelperOptions()
                    );

                    await viewResult.View.RenderAsync(viewContext);
                    return sw.ToString();
                }
            }
        }
    }
}
