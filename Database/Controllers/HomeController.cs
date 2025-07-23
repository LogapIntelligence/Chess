using Database.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace Database.Controllers
{
    public class HomeController : Controller
    {
        private readonly MainContext _context;

        public HomeController(MainContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Dashboard";
            var totalGames = await _context.ChessGames.CountAsync();
            var totalMoves = await _context.ChessMoves.CountAsync();
            var activeBatches = await _context.Batches
                .Where(b => b.Status == "InProgress")
                .Include(b => b.Engine)
                .ToListAsync();

            ViewBag.TotalGames = totalGames;
            ViewBag.TotalMoves = totalMoves;
            ViewBag.ActiveGenerations = activeBatches.Count;

            return View(activeBatches);
        }
    }
}
