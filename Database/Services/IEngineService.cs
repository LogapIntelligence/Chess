using System;
using System.Threading.Tasks;
using Database.Models;

namespace Database.Services
{
    public interface IEngineService
    {
        Task<IEngineInstance> CreateEngineInstanceAsync(string enginePath);
        Task<IBatchProcessor> CreateBatchProcessorAsync(Batch batch);
    }

    public interface IEngineInstance : IDisposable
    {
        string EnginePath { get; }
        bool IsReady { get; }
        Task InitializeAsync();
        Task<string> GetBestMoveAsync(string fen, long movetimeMs); // Changed parameter
        Task<EngineAnalysis> AnalyzePositionAsync(string fen, long movetimeMs); // Changed parameter
        Task QuitAsync();
    }

    public interface IBatchProcessor : IDisposable
    {
        Batch Batch { get; }
        event EventHandler<GameGeneratedEventArgs> GameGenerated;
        event EventHandler<BatchProgressEventArgs> ProgressUpdated;
        Task StartAsync();
        Task StopAsync();
        bool IsRunning { get; }
    }

    public interface IBatchQueueService
    {
        Task EnqueueBatchAsync(Batch batch);
        Task<Batch?> DequeueAsync();
        int GetQueueLength();
        int GetActiveProcessorCount();
        void SetMaxConcurrentProcessors(int max);
    }

    // Event args classes
    public class GameGeneratedEventArgs : EventArgs
    {
        public ChessGame Game { get; set; }
        public long CurrentGameNumber { get; set; }
        public long TotalGames { get; set; }
    }

    public class BatchProgressEventArgs : EventArgs
    {
        public long BatchId { get; set; }
        public long CurrentGames { get; set; }
        public long TotalGames { get; set; }
        public string Status { get; set; }
    }

    // Engine analysis result
    public class EngineAnalysis
    {
        public string BestMove { get; set; }
        public float Evaluation { get; set; }
        public int Depth { get; set; }
        public long Nodes { get; set; }
        public string PrincipalVariation { get; set; }
    }
}