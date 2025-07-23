using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Database.Context;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Database.Services
{
    public class BatchProcessor : IBatchProcessor
    {
        private readonly Batch _batch;
        private readonly IEngineService _engineService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _processingTask;
        private IEngineInstance _engineInstance;
        private bool _disposed;

        public Batch Batch => _batch;
        public bool IsRunning { get; private set; }

        public event EventHandler<GameGeneratedEventArgs> GameGenerated;
        public event EventHandler<BatchProgressEventArgs> ProgressUpdated;

        public BatchProcessor(
            Batch batch,
            IEngineService engineService,
            IServiceProvider serviceProvider,
            ILogger logger)
        {
            _batch = batch;
            _engineService = engineService;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync()
        {
            if (IsRunning) return;

            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // Create engine instance once for the entire batch
            _engineInstance = await _engineService.CreateEngineInstanceAsync(_batch.Engine.FilePath);

            _processingTask = ProcessBatchAsync(_cancellationTokenSource.Token);
        }

        public async Task StopAsync()
        {
            if (!IsRunning) return;

            _cancellationTokenSource?.Cancel();
            if (_processingTask != null)
            {
                await _processingTask;
            }
            IsRunning = false;
        }

        private async Task ProcessBatchAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Starting batch processing: {_batch.BatchId}");

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<MainContext>();
                var chessService = scope.ServiceProvider.GetRequiredService<IChessService>();

                // Update batch status
                _batch.Status = "InProgress";
                context.Update(_batch);
                await context.SaveChangesAsync(cancellationToken);

                for (int gameNumber = 0; gameNumber < _batch.TotalGames && !cancellationToken.IsCancellationRequested; gameNumber++)
                {
                    var game = await GenerateGameAsync(chessService, cancellationToken);

                    if (game != null)
                    {
                        game.BatchId = _batch.Id;
                        context.ChessGames.Add(game);
                        await context.SaveChangesAsync(cancellationToken);

                        // Raise events
                        GameGenerated?.Invoke(this, new GameGeneratedEventArgs
                        {
                            Game = game,
                            CurrentGameNumber = gameNumber + 1,
                            TotalGames = _batch.TotalGames
                        });

                        ProgressUpdated?.Invoke(this, new BatchProgressEventArgs
                        {
                            BatchId = _batch.Id,
                            CurrentGames = gameNumber + 1,
                            TotalGames = _batch.TotalGames,
                            Status = "InProgress"
                        });
                    }
                }

                // Update batch completion
                _batch.Status = cancellationToken.IsCancellationRequested ? "Cancelled" : "Completed";
                _batch.CompletedAt = DateTime.UtcNow;
                context.Update(_batch);
                await context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation($"Batch processing completed: {_batch.BatchId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing batch {_batch.BatchId}");

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<MainContext>();
                _batch.Status = "Failed";
                context.Update(_batch);
                await context.SaveChangesAsync();
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task<ChessGame> GenerateGameAsync(IChessService chessService, CancellationToken cancellationToken)
        {
            try
            {
                // Load standard chess starting position
                chessService.LoadFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                var moves = new List<ChessMove>();
                int moveNumber = 0;
                int consecutiveFailures = 0;
                const int maxConsecutiveFailures = 3;
                const int maxMoves = 300; // Prevent infinite games

                while (!chessService.IsGameOver && moveNumber < maxMoves && !cancellationToken.IsCancellationRequested)
                {
                    var fen = chessService.GetFen();

                    // Get best move from engine
                    var analysis = await _engineInstance.AnalyzePositionAsync(fen, (int)_batch.Depth);

                    if (string.IsNullOrEmpty(analysis.BestMove))
                    {
                        _logger.LogWarning($"Engine returned no move for position: {fen}");
                        consecutiveFailures++;
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            _logger.LogError($"Too many consecutive failures, ending game");
                            break;
                        }
                        continue;
                    }

                    // Apply move
                    if (!chessService.TryApplyMove(analysis.BestMove))
                    {
                        _logger.LogWarning($"Failed to apply move {analysis.BestMove} to position {fen}");
                        consecutiveFailures++;
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            _logger.LogError($"Too many consecutive failures, ending game");
                            break;
                        }
                        continue;
                    }

                    consecutiveFailures = 0; // Reset on successful move
                    moveNumber++;

                    // Store move with analysis data
                    moves.Add(new ChessMove
                    {
                        MoveNumber = moveNumber,
                        Fen = chessService.GetFen(),
                        Evaluation = analysis.Evaluation,
                        ZobristHash = ComputeZobristHash(chessService.GetFen()) // You'll need to implement this
                    });

                    // Check for draw by repetition or 50-move rule (handled by ChessService)
                    if (moveNumber % 50 == 0)
                    {
                        _logger.LogDebug($"Game in progress: {moveNumber} moves");
                    }
                }

                var game = new ChessGame
                {
                    GeneratedAt = DateTime.UtcNow,
                    MoveCount = moveNumber,
                    Result = chessService.GameResult ?? "1/2-1/2",
                    Moves = moves
                };

                _logger.LogInformation($"Game completed: {moveNumber} moves, result: {game.Result}");
                return game;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating game");
                return null;
            }
        }

        private long ComputeZobristHash(string fen)
        {
            // Simple hash function for demonstration
            // In production, implement proper Zobrist hashing
            return fen.GetHashCode();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            StopAsync().Wait(TimeSpan.FromSeconds(10));
            _engineInstance?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}