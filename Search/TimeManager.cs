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
                allocatedTime = limits.MoveTime;
                maxTime = limits.MoveTime;
            }
            else if (limits.Time > 0)
            {
                // Time control with increment
                var timeLeft = limits.Time;
                var increment = limits.Inc;
                var movesToGo = limits.MovesToGo > 0 ? limits.MovesToGo : 30;

                // Basic time allocation
                allocatedTime = CalculateAllocatedTime(timeLeft, increment, movesToGo);
                maxTime = Math.Min(timeLeft / 2, allocatedTime * 4);
            }
            else
            {
                // No time limit
                allocatedTime = long.MaxValue;
                maxTime = long.MaxValue;
            }
        }

        private long CalculateAllocatedTime(long timeLeft, long increment, int movesToGo)
        {
            // Simple time allocation formula
            // Allocate more time in the opening and middle game
            long baseTime;

            if (movesToGo == 1)
            {
                // Last move before time control
                baseTime = timeLeft - 50; // Keep 50ms safety margin
            }
            else
            {
                // Normal time allocation
                baseTime = timeLeft / movesToGo + increment * 3 / 4;

                // Don't use more than 1/5 of remaining time
                baseTime = Math.Min(baseTime, timeLeft / 5);
            }

            // Safety margin
            return Math.Max(1, baseTime - 50);
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