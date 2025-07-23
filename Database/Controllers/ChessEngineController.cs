using Database.Context;
using Database.Models;
using Database.Services;
using Database.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Database.Controllers
{
    public class ChessEngineController : Controller
    {
        private readonly MainContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IBatchQueueService _batchQueueService;

        public ChessEngineController(
            MainContext context,
            IWebHostEnvironment environment,
            IBatchQueueService batchQueueService)
        {
            _context = context;
            _environment = environment;
            _batchQueueService = batchQueueService;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewData["Title"] = "Create New Generation Batch";
            var viewModel = new CreateBatchViewModel
            {
                Engines = await _context.Engines.Where(e => e.IsActive).ToListAsync()
            };
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateBatchViewModel viewModel)
        {
            ModelState.Remove(nameof(CreateBatchViewModel.Engines));
            if (ModelState.IsValid)
            {
                var batch = new Batch
                {
                    BatchId = Guid.NewGuid().ToString("N"),
                    EngineId = viewModel.EngineId,
                    MovetimeMs = viewModel.MovetimeMs, // Changed from Depth
                    TotalGames = viewModel.TotalGames,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Pending",
                    Parameters = "{}" // Add any extra parameters here as JSON
                };

                _context.Batches.Add(batch);
                await _context.SaveChangesAsync();

                var batchWithEngine = _context.Batches.Where(x => x.Id == batch.Id)
                    .Include(x => x.Engine)
                    .FirstOrDefault();

                // Queue the batch for processing
                await _batchQueueService.EnqueueBatchAsync(batchWithEngine);

                TempData["Success"] = $"Batch {batch.BatchId} has been queued for processing.";
                return RedirectToAction("Index", "Home");
            }

            ViewData["Title"] = "Create New Generation Batch";
            viewModel.Engines = await _context.Engines.Where(e => e.IsActive).ToListAsync();
            return View(viewModel);
        }

        [HttpGet]
        public IActionResult Upload()
        {
            ViewData["Title"] = "Upload Chess Engine";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(UploadEngineViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                if (viewModel.EngineFile != null && viewModel.EngineFile.Length > 0)
                {
                    // Validate file extension
                    var allowedExtensions = new[] { ".exe", ".bat", ".cmd" }; // Add other allowed extensions
                    var extension = Path.GetExtension(viewModel.EngineFile.FileName).ToLowerInvariant();

                    if (!allowedExtensions.Contains(extension))
                    {
                        ModelState.AddModelError("EngineFile", "Invalid file type. Please upload an executable file.");
                        ViewData["Title"] = "Upload Chess Engine";
                        return View(viewModel);
                    }

                    var enginesDir = Path.Combine(_environment.ContentRootPath, "ChessEngines");
                    if (!Directory.Exists(enginesDir))
                    {
                        Directory.CreateDirectory(enginesDir);
                    }

                    // Create a unique filename to prevent conflicts
                    var uniqueFileName = $"{Guid.NewGuid()}_{viewModel.EngineFile.FileName}";
                    var filePath = Path.Combine(enginesDir, uniqueFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await viewModel.EngineFile.CopyToAsync(stream);
                    }

                    var engine = new Engine
                    {
                        Name = viewModel.Name,
                        FilePath = filePath,
                        DateAdded = DateTime.UtcNow,
                        IsActive = true
                    };

                    _context.Engines.Add(engine);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = $"Engine '{engine.Name}' has been uploaded successfully.";
                    return RedirectToAction("Index", "Home");
                }
                ModelState.AddModelError("EngineFile", "Please select a file to upload.");
            }

            ViewData["Title"] = "Upload Chess Engine";
            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Status()
        {
            ViewData["Title"] = "Batch Processing Status";

            var activeBatches = await _context.Batches
                .Include(b => b.Engine)
                .Include(b => b.Games)
                .Where(b => b.Status == "InProgress" || b.Status == "Pending")
                .OrderBy(b => b.CreatedAt)
                .ToListAsync();

            ViewBag.QueueLength = _batchQueueService.GetQueueLength();
            ViewBag.ActiveProcessors = _batchQueueService.GetActiveProcessorCount();

            return View(activeBatches);
        }
    }
}