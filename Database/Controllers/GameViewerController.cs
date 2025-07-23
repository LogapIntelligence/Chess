using Database.Context;
using Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Database.Controllers
{
    public class GameViewerController : Controller
    {
        private readonly MainContext _context;

        public GameViewerController(MainContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(long? gameId)
        {
            ViewData["Title"] = "Game Viewer";

            ChessGame game = null;
            if (gameId.HasValue)
            {
                game = await GetGameWithMovesAsync(gameId.Value);
            }
            else
            {
                // Load a random game if no specific game requested
                game = await GetRandomGameAsync();
            }

            return View(game);
        }

        [HttpGet]
        public async Task<IActionResult> GetGame(long gameId)
        {
            var game = await GetGameWithMovesAsync(gameId);
            if (game == null)
            {
                return NotFound();
            }

            return Json(new
            {
                id = game.Id,
                result = game.Result,
                moveCount = game.MoveCount,
                moves = game.Moves.OrderBy(m => m.MoveNumber).Select(m => new
                {
                    moveNumber = m.MoveNumber,
                    fen = m.Fen,
                    evaluation = m.Evaluation
                }).ToList()
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetRandomGame()
        {
            var game = await GetRandomGameAsync();
            if (game == null)
            {
                return NotFound();
            }

            return Json(new
            {
                id = game.Id,
                result = game.Result,
                moveCount = game.MoveCount,
                moves = game.Moves.OrderBy(m => m.MoveNumber).Select(m => new
                {
                    moveNumber = m.MoveNumber,
                    fen = m.Fen,
                    evaluation = m.Evaluation
                }).ToList()
            });
        }

        [HttpGet]
        public async Task<IActionResult> SearchGames(string query, int page = 1, int pageSize = 20)
        {
            var gamesQuery = _context.ChessGames.AsQueryable();

            // Search by game result or ID
            if (!string.IsNullOrEmpty(query))
            {
                gamesQuery = gamesQuery.Where(g =>
                    g.Result.Contains(query) ||
                    g.Id.ToString().Contains(query));
            }

            var totalGames = await gamesQuery.CountAsync();
            var games = await gamesQuery
                .OrderByDescending(g => g.GeneratedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new
                {
                    id = g.Id,
                    result = g.Result,
                    moveCount = g.MoveCount,
                    generatedAt = g.GeneratedAt,
                    batchId = g.BatchId
                })
                .ToListAsync();

            return Json(new
            {
                games = games,
                totalGames = totalGames,
                currentPage = page,
                totalPages = (int)Math.Ceiling(totalGames / (double)pageSize)
            });
        }

        private async Task<ChessGame> GetGameWithMovesAsync(long gameId)
        {
            return await _context.ChessGames
                .Include(g => g.Moves)
                .FirstOrDefaultAsync(g => g.Id == gameId);
        }

        private async Task<ChessGame> GetRandomGameAsync()
        {
            // Get a random game that has moves and a definitive result
            var gameIds = await _context.ChessGames
                .Where(g => g.MoveCount > 0 && g.Result != "*")
                .Select(g => g.Id)
                .ToListAsync();

            if (!gameIds.Any())
                return null;

            var random = new Random();
            var randomId = gameIds[random.Next(gameIds.Count)];

            return await GetGameWithMovesAsync(randomId);
        }
    }
}