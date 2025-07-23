using Database.Models;
using System.Threading.Tasks;

namespace Database.Services
{
    public interface IEngineService
    {
        Task<string> GetBestMoveAsync(string fen, string enginePath, int depth);
        Task StartGenerationBatch(Batch batch);
    }
}
