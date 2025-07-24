using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chess
{
    /// <summary>
    /// Interface for NNUE evaluators
    /// </summary>
    public interface INNUEEvaluator
    {
        /// <summary>
        /// Check if the NNUE model is loaded and ready
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// Evaluate a chess position
        /// </summary>
        /// <param name="board">The board position to evaluate</param>
        /// <returns>Evaluation in centipawns from white's perspective</returns>
        int Evaluate(ref Board board);
    }
}
