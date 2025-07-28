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
        private volatile bool forceStop = false;

        public TimeManager()
        {
            stopwatch = new Stopwatch();
        }

        public void StartSearch(SearchLimits limits, Color sideToMove)
        {
            stopwatch.Restart();
            startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            infinite = limits.Infinite;
            forceStop = false;

            if (limits.MoveTime > 0)
            {
                // Fixed time per move - use exactly what was specified
                allocatedTime = Math.Max(10, limits.MoveTime - 50); // Small safety margin
                maxTime = limits.MoveTime;
            }
            else if (limits.Time > 0 && !infinite)
            {
                // Time control with increment
                var timeLeft = limits.Time;
                var increment = limits.Inc;
                var movesToGo = limits.MovesToGo > 0 ? limits.MovesToGo : 30;

                // More conservative time allocation for GUI compatibility
                allocatedTime = CalculateAllocatedTime(timeLeft, increment, movesToGo);
                maxTime = Math.Min(timeLeft / 2, allocatedTime * 4);

                // Ensure we never use all the time
                maxTime = Math.Min(maxTime, timeLeft - 100);
                allocatedTime = Math.Min(allocatedTime, maxTime);
            }
            else if (limits.Depth < 128 && !infinite)
            {
                // Depth-limited search - allow reasonable time
                allocatedTime = 60000; // 1 minute
                maxTime = 120000; // 2 minutes max
            }
            else if (infinite)
            {
                // Infinite analysis
                allocatedTime = long.MaxValue;
                maxTime = long.MaxValue;
            }
            else
            {
                // Default fallback
                allocatedTime = 5000; // 5 seconds
                maxTime = 15000; // 15 seconds max
            }

            // Ensure minimum values
            if (!infinite)
            {
                allocatedTime = Math.Max(10, allocatedTime);
                maxTime = Math.Max(allocatedTime, maxTime);
            }
        }

        private long CalculateAllocatedTime(long timeLeft, long increment, int movesToGo)
        {
            // Emergency time handling
            if (timeLeft < 1000) // Less than 1 second
            {
                return Math.Max(10, timeLeft / 4);
            }

            // Very low time - be extra conservative  
            if (timeLeft < 5000) // Less than 5 seconds
            {
                return Math.Max(50, timeLeft / 10 + increment / 2);
            }

            // Calculate base time allocation
            long baseTime;

            if (movesToGo <= 1)
            {
                // Last move before time control - use most of remaining time
                baseTime = Math.Max(100, timeLeft - 200);
            }
            else if (movesToGo <= 5)
            {
                // Few moves left - be more conservative
                baseTime = timeLeft / (movesToGo + 2) + increment / 2;
            }
            else
            {
                // Normal time allocation
                baseTime = timeLeft / Math.Max(25, movesToGo) + increment * 3 / 4;
            }

            // Don't use more than 1/10 of remaining time in normal situations
            baseTime = Math.Min(baseTime, timeLeft / 10);

            return Math.Max(50, baseTime);
        }

        public bool ShouldStop()
        {
            if (forceStop)
                return true;

            if (infinite)
                return false;

            return ElapsedMs() >= allocatedTime;
        }

        public bool ShouldStopHard()
        {
            if (forceStop)
                return true;

            if (infinite)
                return false;

            return ElapsedMs() >= maxTime;
        }

        public void ForceStop()
        {
            forceStop = true;
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
            if (!infinite)
            {
                allocatedTime = Math.Max(10, time);
            }
        }

        public void ExtendTime(int factor)
        {
            if (!infinite)
            {
                allocatedTime = Math.Min(allocatedTime * factor, maxTime);
            }
        }
    }
}