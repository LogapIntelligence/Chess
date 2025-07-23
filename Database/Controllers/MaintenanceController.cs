using Database.Context;
using Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Database.Controllers
{
    public class MaintenanceController : Controller
    {
        private readonly MainContext _context;
        private readonly ILogger<MaintenanceController> _logger;

        public MaintenanceController(MainContext context, ILogger<MaintenanceController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            ViewData["Title"] = "Database Maintenance";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> FixGameResults()
        {
            var startTime = DateTime.UtcNow;
            var gamesFixed = 0;
            var gamesChecked = 0;
            var errors = 0;

            try
            {
                var totalGames = await _context.ChessGames.CountAsync();
                _logger.LogInformation($"Starting to fix game results for {totalGames} games...");

                // Process in batches to avoid memory issues
                const int batchSize = 100;
                for (int skip = 0; skip < totalGames; skip += batchSize)
                {
                    var games = await _context.ChessGames
                        .Include(g => g.Moves)
                        .OrderBy(g => g.Id)
                        .Skip(skip)
                        .Take(batchSize)
                        .ToListAsync();

                    foreach (var game in games)
                    {
                        gamesChecked++;

                        try
                        {
                            if (game.Moves == null || !game.Moves.Any())
                            {
                                _logger.LogWarning($"Game {game.Id} has no moves");
                                continue;
                            }

                            // Get the last move
                            var lastMove = game.Moves.OrderByDescending(m => m.MoveNumber).FirstOrDefault();
                            if (lastMove == null)
                                continue;

                            string newResult = DetermineGameResult(lastMove.Evaluation, game.Result);

                            // Update if needed
                            if (newResult != null && game.Result != newResult)
                            {
                                _logger.LogInformation($"Fixing game {game.Id}: {game.Result} -> {newResult} (eval: {lastMove.Evaluation})");
                                game.Result = newResult;
                                gamesFixed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error processing game {game.Id}");
                            errors++;
                        }
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Processed {Math.Min(skip + batchSize, totalGames)} / {totalGames} games");
                }

                var duration = DateTime.UtcNow - startTime;
                var message = $"Maintenance completed in {duration.TotalSeconds:F1} seconds. " +
                             $"Checked: {gamesChecked}, Fixed: {gamesFixed}, Errors: {errors}";

                _logger.LogInformation(message);
                TempData["Success"] = message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during game result fixing");
                TempData["Error"] = $"Error: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private string DetermineGameResult(float evaluation, string currentResult)
        {
            // Check for mate evaluations
            const float MATE_THRESHOLD = 9990f;

            if (evaluation >= MATE_THRESHOLD)
            {
                return "1-0"; // White wins
            }
            else if (evaluation <= -MATE_THRESHOLD)
            {
                return "0-1"; // Black wins
            }
            else if (currentResult == "*")
            {
                // If game was marked as ongoing but has finished moves, it's likely a draw
                return "1/2-1/2";
            }

            return null; // No change needed
        }

        [HttpGet]
        public async Task<IActionResult> DatabaseStats()
        {
            var stats = new
            {
                TotalGames = await _context.ChessGames.CountAsync(),
                TotalMoves = await _context.ChessMoves.CountAsync(),
                TotalBatches = await _context.Batches.CountAsync(),
                TotalEngines = await _context.Engines.CountAsync(),
                GameResults = await _context.ChessGames
                    .GroupBy(g => g.Result)
                    .Select(g => new { Result = g.Key, Count = g.Count() })
                    .ToListAsync(),
                UnfinishedGames = await _context.ChessGames.CountAsync(g => g.Result == "*"),
                GamesWithoutMoves = await _context.ChessGames.CountAsync(g => !g.Moves.Any())
            };

            return Json(stats);
        }
    }
}