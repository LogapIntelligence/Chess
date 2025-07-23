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
        private readonly IEngineService _engineService;

        public ChessEngineController(MainContext context, IWebHostEnvironment environment, IEngineService engineService)
        {
            _context = context;
            _environment = environment;
            _engineService = engineService;
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
                    Depth = viewModel.Depth,
                    TotalGames = viewModel.TotalGames,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Pending",
                    Parameters = "{}" // Add any extra parameters here as JSON
                };

                _context.Batches.Add(batch);
                await _context.SaveChangesAsync();

                // The background service will pick this up.
                // We could also directly trigger it via the service.
                await _engineService.StartGenerationBatch(batch);

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
                    var enginesDir = Path.Combine(_environment.ContentRootPath, "ChessEngines");
                    if (!Directory.Exists(enginesDir))
                    {
                        Directory.CreateDirectory(enginesDir);
                    }

                    var filePath = Path.Combine(enginesDir, viewModel.EngineFile.FileName);

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

                    return RedirectToAction("Index", "Home");
                }
                ModelState.AddModelError("EngineFile", "Please select a file to upload.");
            }

            ViewData["Title"] = "Upload Chess Engine";
            return View(viewModel);
        }
    }
}
