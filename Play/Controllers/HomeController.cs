using Microsoft.AspNetCore.Mvc;
using Play.Models;
using Play.Services;
using System.Diagnostics;

namespace Play.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUciEngineService _engineService;

        public HomeController(ILogger<HomeController> logger, IUciEngineService engineService)
        {
            _logger = logger;
            _engineService = engineService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> LoadEngine([FromBody] string enginePath)
        {
            try
            {
                // 1. Trim whitespace from the beginning and end of the string.
                string cleanedPath = enginePath.Trim();

                // 2. Remove leading/trailing double quotes if present.
                // This handles cases where the path might be wrapped in quotes like "C:\engine\stockfish.exe"
                if (cleanedPath.StartsWith("\"") && cleanedPath.EndsWith("\""))
                {
                    cleanedPath = cleanedPath.Substring(1, cleanedPath.Length - 2);
                }

                // 3. Unescape common escape sequences (optional, but good for robustness).
                // This is more relevant if the path was read from a text file or a less controlled input.
                // For a direct HTML input, it's less likely, but harmless to include.
                cleanedPath = Uri.UnescapeDataString(cleanedPath); // Decodes URL-escaped characters
                cleanedPath = cleanedPath.Replace("\\\\", "\\"); // Replace double backslashes with single (if escaped)
                cleanedPath = cleanedPath.Replace(@"\\", @"\"); // Another way to handle double backslashes


                // 4. (Consider adding) Path validation:
                // You might want to add checks here, e.g., to ensure it's a valid file path format,
                // or that the file actually exists, though `LoadEngineAsync` might handle existence.
                // Example:
                // if (!System.IO.Path.IsPathRooted(cleanedPath) || !System.IO.File.Exists(cleanedPath))
                // {
                //     return Json(new { success = false, message = "Invalid or non-existent engine path provided." });
                // }

                var success = await _engineService.LoadEngineAsync(cleanedPath);
                return Json(new { success, message = success ? "Engine loaded successfully" : "Failed to load engine" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading engine");
                // For security, avoid returning raw exception messages to the client in production.
                // A generic error message is often better, and log the full exception internally.
                return Json(new { success = false, message = "An error occurred while attempting to load the engine." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UnloadEngine()
        {
            try
            {
                await _engineService.UnloadEngineAsync();
                return Json(new { success = true, message = "Engine unloaded" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unloading engine");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}