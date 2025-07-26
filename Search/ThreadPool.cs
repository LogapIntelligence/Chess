using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Move;

namespace Search
{
    public class ThreadPool
    {
        private readonly SearchThread[] threads;
        private readonly int threadCount;
        private CancellationTokenSource? cancellationTokenSource;

        public ThreadPool(int numThreads)
        {
            threadCount = Math.Max(1, Math.Min(numThreads, Environment.ProcessorCount));
            threads = new SearchThread[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                threads[i] = new SearchThread(i);
            }
        }

        public async Task<SearchResult> StartSearch(Position position, SearchLimits limits,
                                                   TranspositionTable tt, TimeManager timeManager)
        {
            cancellationTokenSource = new CancellationTokenSource();
            var tasks = new Task<SearchResult>[threadCount];

            // Start all threads
            for (int i = 0; i < threadCount; i++)
            {
                var threadId = i;
                tasks[i] = Task.Run(() =>
                    threads[threadId].Search(position, limits, tt, timeManager, cancellationTokenSource.Token));
            }

            // Wait for main thread to finish
            var mainResult = await tasks[0];

            // Stop all other threads
            cancellationTokenSource.Cancel();

            // Wait for all threads to finish
            await Task.WhenAll(tasks);

            // Aggregate statistics
            ulong totalNodes = 0;
            foreach (var thread in threads)
            {
                totalNodes += thread.NodesSearched;
            }

            mainResult.Nodes = totalNodes;
            return mainResult;
        }

        public void StopSearch()
        {
            cancellationTokenSource?.Cancel();
        }

        public void Clear()
        {
            foreach (var thread in threads)
            {
                thread.Clear();
            }
        }
    }

    public class SearchThread
    {
        private readonly int threadId;
        private readonly Search search;

        public ulong NodesSearched => search.NodesSearched;

        public SearchThread(int id)
        {
            threadId = id;
            search = new Search();
        }

        public SearchResult Search(Position position, SearchLimits limits,
                                 TranspositionTable tt, TimeManager timeManager,
                                 CancellationToken cancellationToken)
        {
            // Clone position for thread safety
            var threadPosition = new Position(position);

            // Adjust limits for helper threads
            if (threadId > 0)
            {
                limits = new SearchLimits
                {
                    Depth = limits.Depth,
                    Infinite = true, // Helper threads search until stopped
                    Time = long.MaxValue
                };
            }

            // Start search
            using (cancellationToken.Register(() => search.StopSearch()))
            {
                return search.StartSearch(threadPosition, limits);
            }
        }

        public void Clear()
        {
            // Clear thread-specific data
        }
    }
}