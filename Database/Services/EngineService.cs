using System.Diagnostics;
using System.Threading.Tasks;
using Database.Models;

namespace Database.Services
{
    public class EngineService : IEngineService
    {
        public async Task<string> GetBestMoveAsync(string fen, string enginePath, int depth)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = enginePath,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            await process.StandardInput.WriteLineAsync("uci");
            await process.StandardInput.WriteLineAsync($"position fen {fen}");
            await process.StandardInput.WriteLineAsync($"go depth {depth}");

            string bestMove = null;
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 5000) // 5 second timeout
            {
                if (process.StandardOutput.EndOfStream) break;
                var line = await process.StandardOutput.ReadLineAsync();
                if (line != null && line.StartsWith("bestmove"))
                {
                    bestMove = line.Split(' ')[1];
                    break;
                }
            }

            await process.StandardInput.WriteLineAsync("quit");
            process.WaitForExit();
            return bestMove;
        }

        public Task StartGenerationBatch(Batch batch)
        {
            // This method is a placeholder to demonstrate the service interface.
            // The actual generation is handled by MoveGenerationService.
            // In a more complex scenario, this service could be responsible for
            // queuing the batch in a message queue like RabbitMQ or Azure Service Bus.
            return Task.CompletedTask;
        }
    }
}
