using System;
using System.Diagnostics;
using Move;

namespace Search
{
    public class TimeManager
    {
        private readonly Stopwatch stopwatch;
        private long allocatedTime;
        private long maxTime;
        private long startTime;
        private bool infinite;

        public TimeManager()
        {
            stopwatch = new Stopwatch();
        }

        public void StartSearch(SearchLimits limits, Color sideToMove)
        {
            stopwatch.Restart();
            startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            infinite = limits.Infinite;

            if (limits.MoveTime > 0)
            {
                // Fixed time per move
                allocatedTime = limits.MoveTime - 50; // 50ms safety margin
                maxTime = limits.MoveTime;
            }
            else if (limits.Time > 0)
            {
                // Time control with increment
                var timeLeft = limits.Time;
                var increment = limits.Inc;
                var movesToGo = limits.MovesToGo > 0 ? limits.MovesToGo : 25; // More conservative

                // Basic time allocation - be more conservative
                allocatedTime = CalculateAllocatedTime(timeLeft, increment, movesToGo);
                maxTime = Math.Min(timeLeft / 3, allocatedTime * 3); // More conservative max time
            }
            else if (limits.Depth < 128) // Depth-only search
            {
                // For depth-limited search, give reasonable time limits
                allocatedTime = 30000; // 30 seconds max
                maxTime = 60000; // 1 minute absolute max
            }
            else
            {
                // No time limit - but set reasonable defaults to prevent infinite search
                allocatedTime = int.MaxValue; // 10 seconds default
                maxTime = int.MaxValue; // 30 seconds max
            }
        }

        private long CalculateAllocatedTime(long timeLeft, long increment, int movesToGo)
        {
            // More conservative time allocation
            long baseTime;

            if (movesToGo == 1)
            {
                // Last move before time control
                baseTime = Math.Max(100, timeLeft - 100); // Keep 100ms safety margin
            }
            else
            {
                // Normal time allocation - use less time per move
                baseTime = timeLeft / (movesToGo + 5) + increment / 2;

                // Don't use more than 1/8 of remaining time
                baseTime = Math.Min(baseTime, timeLeft / 8);
            }

            // Always keep some safety margin
            return Math.Max(50, baseTime - 100);
        }

        public bool ShouldStop()
        {
            if (infinite)
                return false;

            return ElapsedMs() >= allocatedTime;
        }

        public bool ShouldStopHard()
        {
            if (infinite)
                return false;

            return ElapsedMs() >= maxTime;
        }

        public long ElapsedMs()
        {
            return stopwatch.ElapsedMilliseconds;
        }

        public long GetAllocatedTime()
        {
            return allocatedTime;
        }

        public void SetAllocatedTime(long time)
        {
            allocatedTime = time;
        }

        public void ExtendTime(int factor)
        {
            allocatedTime = Math.Min(allocatedTime * factor, maxTime);
        }
    }
}