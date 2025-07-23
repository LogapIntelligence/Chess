using Database.Context;
using Database.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace Database.Controllers
{
    public class HomeController : Controller
    {
        private readonly MainContext _context;
        private readonly IBatchQueueService _batchQueueService;

        public HomeController(MainContext context, IBatchQueueService batchQueueService)
        {
            _context = context;
            _batchQueueService = batchQueueService;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Dashboard";

            // Get counts with null safety
            var totalGames = await _context.ChessGames.CountAsync();
            var totalMoves = await _context.ChessMoves.CountAsync();

            // Get active batches
            var activeBatches = await _context.Batches
                .Where(b => b.Status == "InProgress")
                .Include(b => b.Engine)
                .Include(b => b.Games)
                .ToListAsync();

            ViewBag.TotalGames = totalGames;
            ViewBag.TotalMoves = totalMoves;

            // Use the batch queue service to get the actual count of active processors
            // This ensures consistency with SignalR updates
            ViewBag.ActiveGenerations = _batchQueueService.GetActiveProcessorCount();

            return View(activeBatches);
        }
    }
}