using Database.Configuration;
using Database.Context;
using Database.Hubs;
using Database.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Database.Services
{
    public class BatchQueueService : BackgroundService, IBatchQueueService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BatchQueueService> _logger;
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly ConcurrentQueue<Batch> _batchQueue = new();
        private readonly List<IBatchProcessor> _activeProcessors = new();
        private readonly SemaphoreSlim _processorSemaphore;
        private int _maxConcurrentProcessors = 2;
        private readonly IOptions<BatchProcessingOptions> _options;

        public BatchQueueService(
        IServiceProvider serviceProvider,
        ILogger<BatchQueueService> logger,
        IHubContext<DashboardHub> hubContext,
        IOptions<BatchProcessingOptions> options)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _hubContext = hubContext;
            _options = options;
            _maxConcurrentProcessors = options.Value.MaxConcurrentProcessors;
            _processorSemaphore = new SemaphoreSlim(_maxConcurrentProcessors, _maxConcurrentProcessors);
        }

        public Task EnqueueBatchAsync(Batch batch)
        {
            _batchQueue.Enqueue(batch);
            _logger.LogInformation($"Batch {batch.BatchId} enqueued. Queue length: {_batchQueue.Count}");
            return Task.CompletedTask;
        }

        public Task<Batch?> DequeueAsync()
        {
            _batchQueue.TryDequeue(out var batch);
            return Task.FromResult(batch);
        }

        public int GetQueueLength() => _batchQueue.Count;

        public int GetActiveProcessorCount()
        {
            lock (_activeProcessors)
            {
                return _activeProcessors.Count(p => p.IsRunning);
            }
        }

        public void SetMaxConcurrentProcessors(int max)
        {
            if (max < 1) max = 1;
            if (max > 10) max = 10; // Reasonable upper limit

            _maxConcurrentProcessors = max;
            _logger.LogInformation($"Max concurrent processors set to {max}");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Batch Queue Service started");

            // Load any pending batches from database on startup
            await LoadPendingBatchesAsync();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check for new pending batches in database
                    await CheckForNewBatchesAsync(stoppingToken);

                    // Process queued batches
                    await ProcessQueuedBatchesAsync(stoppingToken);

                    // Clean up completed processors
                    await CleanupCompletedProcessorsAsync();

                    // Broadcast status update
                    await BroadcastStatusUpdateAsync();

                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in batch queue service");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            // Cleanup on shutdown
            await ShutdownProcessorsAsync();
            _logger.LogInformation("Batch Queue Service stopped");
        }

        private async Task LoadPendingBatchesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MainContext>();

            var pendingBatches = await context.Batches
                .Include(b => b.Engine)
                .Where(b => b.Status == "Pending" || b.Status == "InProgress")
                .OrderBy(b => b.CreatedAt)
                .ToListAsync();

            foreach (var batch in pendingBatches)
            {
                if (batch.Status == "InProgress")
                {
                    // Reset in-progress batches to pending on startup
                    batch.Status = "Pending";
                    context.Update(batch);
                }
                _batchQueue.Enqueue(batch);
            }

            await context.SaveChangesAsync();
            _logger.LogInformation($"Loaded {pendingBatches.Count} pending batches");
        }

        private async Task CheckForNewBatchesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MainContext>();

            var newBatches = await context.Batches
                .Include(b => b.Engine)
                .Where(b => b.Status == "Pending")
                .OrderBy(b => b.CreatedAt)
                .ToListAsync(cancellationToken);

            var queuedBatchIds = _batchQueue.Select(b => b.Id).ToHashSet();

            foreach (var batch in newBatches.Where(b => !queuedBatchIds.Contains(b.Id)))
            {
                _batchQueue.Enqueue(batch);
                _logger.LogInformation($"Found new batch {batch.BatchId}");
            }
        }

        private async Task ProcessQueuedBatchesAsync(CancellationToken cancellationToken)
        {
            while (_batchQueue.TryPeek(out var batch) && !cancellationToken.IsCancellationRequested)
            {
                if (await _processorSemaphore.WaitAsync(0))
                {
                    try
                    {
                        if (_batchQueue.TryDequeue(out batch))
                        {
                            await StartBatchProcessorAsync(batch, cancellationToken);
                        }
                        else
                        {
                            _processorSemaphore.Release();
                        }
                    }
                    catch
                    {
                        _processorSemaphore.Release();
                        throw;
                    }
                }
                else
                {
                    // All processor slots are full
                    break;
                }
            }
        }

        private async Task StartBatchProcessorAsync(Batch batch, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var engineService = scope.ServiceProvider.GetRequiredService<IEngineService>();

                var processor = await engineService.CreateBatchProcessorAsync(batch);

                // Subscribe to events
                processor.GameGenerated += OnGameGenerated;
                processor.ProgressUpdated += OnProgressUpdated;

                lock (_activeProcessors)
                {
                    _activeProcessors.Add(processor);
                }

                // Start processing in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await processor.StartAsync();
                    }
                    finally
                    {
                        _processorSemaphore.Release();
                    }
                }, cancellationToken);

                _logger.LogInformation($"Started processor for batch {batch.BatchId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to start processor for batch {batch.BatchId}");
                _processorSemaphore.Release();

                // Re-queue the batch
                _batchQueue.Enqueue(batch);
            }
        }

        private async Task CleanupCompletedProcessorsAsync()
        {
            List<IBatchProcessor> toRemove;

            lock (_activeProcessors)
            {
                toRemove = _activeProcessors.Where(p => !p.IsRunning).ToList();
                foreach (var processor in toRemove)
                {
                    _activeProcessors.Remove(processor);
                }
            }

            foreach (var processor in toRemove)
            {
                processor.GameGenerated -= OnGameGenerated;
                processor.ProgressUpdated -= OnProgressUpdated;
                processor.Dispose();
            }

            if (toRemove.Any())
            {
                _logger.LogInformation($"Cleaned up {toRemove.Count} completed processors");
            }
        }

        private async Task ShutdownProcessorsAsync()
        {
            List<IBatchProcessor> processors;

            lock (_activeProcessors)
            {
                processors = _activeProcessors.ToList();
                _activeProcessors.Clear();
            }

            var stopTasks = processors.Select(p => p.StopAsync()).ToArray();
            await Task.WhenAll(stopTasks);

            foreach (var processor in processors)
            {
                processor.Dispose();
            }

            _logger.LogInformation($"Shut down {processors.Count} processors");
        }

        private void OnGameGenerated(object sender, GameGeneratedEventArgs e)
        {
            Task.Run(async () =>
            {
                await _hubContext.Clients.All.SendAsync("GameGenerated", new
                {
                    batchId = e.Game.BatchId,
                    gameId = e.Game.Id,
                    moveCount = e.Game.MoveCount,
                    result = e.Game.Result,
                    currentGame = e.CurrentGameNumber,
                    totalGames = e.TotalGames
                });
            });
        }

        private void OnProgressUpdated(object sender, BatchProgressEventArgs e)
        {
            Task.Run(async () =>
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", new
                {
                    batchId = e.BatchId,
                    currentGames = e.CurrentGames,
                    totalGames = e.TotalGames,
                    status = e.Status
                });
            });
        }

        private async Task BroadcastStatusUpdateAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MainContext>();

            var stats = new
            {
                totalGames = await context.ChessGames.CountAsync(),
                totalMoves = await context.ChessMoves.CountAsync(),
                activeGenerations = GetActiveProcessorCount(),
                queueLength = GetQueueLength()
            };

            await _hubContext.Clients.All.SendAsync("ReceiveDashboardUpdate", stats);
        }

        public override void Dispose()
        {
            _processorSemaphore?.Dispose();
            base.Dispose();
        }
    }
}