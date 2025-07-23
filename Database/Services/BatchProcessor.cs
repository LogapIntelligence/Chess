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
                bool gameEnded = false;
                string gameResult = "*";

                while (!gameEnded && moveNumber < maxMoves && !cancellationToken.IsCancellationRequested)
                {
                    var fen = chessService.GetFen();

                    // Check if game is already over BEFORE asking engine for a move
                    if (chessService.IsGameOver)
                    {
                        gameResult = chessService.GameResult;
                        gameEnded = true;
                        _logger.LogInformation($"Game ended by rule: {gameResult} after {moveNumber} moves");

                        // Store the final position
                        moves.Add(new ChessMove
                        {
                            MoveNumber = moveNumber + 1,
                            Fen = fen,
                            Evaluation = 0, // Draw evaluation for rule-based draws
                            Depth = 0, // No search depth for final position
                            ZobristHash = ComputeZobristHash(fen)
                        });
                        break;
                    }

                    // Get best move from engine only if game is not over
                    var analysis = await _engineInstance.AnalyzePositionAsync(fen, _batch.MovetimeMs); // Use MovetimeMs

                    // Check if the position is checkmate based on evaluation
                    // Mate evaluations are typically 10000 - moves_to_mate
                    if (Math.Abs(analysis.Evaluation) >= 9990)
                    {
                        // Store the final position
                        moves.Add(new ChessMove
                        {
                            MoveNumber = moveNumber + 1,
                            Fen = fen,
                            Evaluation = analysis.Evaluation,
                            Depth = analysis.Depth, // Store actual depth reached
                            ZobristHash = ComputeZobristHash(fen)
                        });

                        // Determine winner based on evaluation and whose turn it is
                        var fenParts = fen.Split(' ');
                        bool isWhiteToMove = fenParts.Length > 1 && fenParts[1] == "w";

                        if (analysis.Evaluation > 9990)
                        {
                            // Positive mate score means the side to move has a winning position
                            gameResult = isWhiteToMove ? "1-0" : "0-1";
                        }
                        else
                        {
                            // Negative mate score means the side to move is being mated
                            gameResult = isWhiteToMove ? "0-1" : "1-0";
                        }

                        gameEnded = true;
                        _logger.LogInformation($"Game ended in checkmate: {gameResult} after {moveNumber} moves");
                        break;
                    }

                    if (string.IsNullOrEmpty(analysis.BestMove))
                    {
                        _logger.LogWarning($"Engine returned no move for position: {fen}");
                        consecutiveFailures++;
                        if (consecutiveFailures >= maxConsecutiveFailures)
                        {
                            _logger.LogError($"Too many consecutive failures, ending game");
                            gameResult = "1/2-1/2"; // Assume draw if engine fails
                            gameEnded = true;
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
                            gameResult = "1/2-1/2"; // Assume draw if moves fail
                            gameEnded = true;
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
                        Depth = analysis.Depth, // Store actual depth reached
                        ZobristHash = ComputeZobristHash(chessService.GetFen())
                    });

                    // Check if ChessService detected a draw after the move
                    if (chessService.IsGameOver)
                    {
                        gameResult = chessService.GameResult;
                        gameEnded = true;
                        _logger.LogInformation($"Game ended by rule: {gameResult} after {moveNumber} moves");
                        break;
                    }

                    // Additional draw detection based on move count
                    if (moveNumber >= maxMoves)
                    {
                        gameResult = "1/2-1/2";
                        gameEnded = true;
                        _logger.LogInformation($"Game ended by max moves limit: {moveNumber} moves");
                        break;
                    }

                    // Log progress
                    if (moveNumber % 50 == 0)
                    {
                        _logger.LogDebug($"Game in progress: {moveNumber} moves, halfmove clock: {fen.Split(' ')[4]}");
                    }
                }

                var game = new ChessGame
                {
                    GeneratedAt = DateTime.UtcNow,
                    MoveCount = moveNumber,
                    Result = gameResult,
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