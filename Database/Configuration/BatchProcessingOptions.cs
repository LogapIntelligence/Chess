namespace Database.Configuration
{
    public class BatchProcessingOptions
    {
        public int MaxConcurrentProcessors { get; set; } = 2;
        public int DefaultSearchDepth { get; set; } = 10;
        public int EngineTimeoutSeconds { get; set; } = 30;
        public int MaxMovesPerGame { get; set; } = 300;
    }
}