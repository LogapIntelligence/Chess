using Move;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Perft
{
    public static class PerftOptimized
    {
        // Template method pattern to eliminate color checking
        private static unsafe ulong PerftWhite(Position p, uint depth, Move.Move* moveBuffer)
        {
            if (depth == 0) return 1;

            int moveCount = p.GenerateLegalsInto<White>(moveBuffer);
            if (depth == 1) return (ulong)moveCount;

            ulong nodes = 0;
            for (int i = 0; i < moveCount; i++)
            {
                p.Play(Color.White, moveBuffer[i]);
                nodes += PerftBlack(p, depth - 1, moveBuffer + 256); // Use different part of buffer
                p.Undo(Color.White, moveBuffer[i]);
            }
            return nodes;
        }

        private static unsafe ulong PerftBlack(Position p, uint depth, Move.Move* moveBuffer)
        {
            if (depth == 0) return 1;

            int moveCount = p.GenerateLegalsInto<Black>(moveBuffer);
            if (depth == 1) return (ulong)moveCount;

            ulong nodes = 0;
            for (int i = 0; i < moveCount; i++)
            {
                p.Play(Color.Black, moveBuffer[i]);
                nodes += PerftWhite(p, depth - 1, moveBuffer + 256);
                p.Undo(Color.Black, moveBuffer[i]);
            }
            return nodes;
        }

        public static unsafe ulong Run(Position p, uint depth)
        {
            // Allocate enough buffer for all recursion levels
            Move.Move* buffer = stackalloc Move.Move[256 * (int)depth];

            return p.Turn == Color.White
                ? PerftWhite(p, depth, buffer)
                : PerftBlack(p, depth, buffer);
        }
    }
}
