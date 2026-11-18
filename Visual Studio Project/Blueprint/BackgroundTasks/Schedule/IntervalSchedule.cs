// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.BackgroundTasks.Schedule
{
    /// <summary>Schedules executions at fixed intervals from activation.</summary>
    public sealed class IntervalSchedule : BackgroundTaskSchedule
    {
        private readonly TimeSpan fInterval;

        /// <summary>Sets the positive repetition interval.</summary>
        /// <param name="interval">The interval, with the first execution after it has elapsed.</param>
        /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
        public IntervalSchedule(TimeSpan interval)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
            fInterval = interval;
        }

        /// <summary>The interval between scheduled executions.</summary>
        public TimeSpan Interval
        {
            get
            {
                return fInterval;
            }
        }

        /// <summary>Calculates the next interval boundary, skipping those that have passed.</summary>
        /// <param name="after">The time after which to find an execution.</param>
        /// <param name="activatedAt">The starting point from which intervals are measured.</param>
        /// <returns>The next execution time in UTC, or null if it exceeds the date range.</returns>
        public override DateTimeOffset? CalculateNextRunTime(DateTimeOffset after, DateTimeOffset activatedAt)
        {
            var elapsedTicks = after.UtcTicks - activatedAt.UtcTicks;
            var intervalCount = elapsedTicks < 0 ? 1 : elapsedTicks / fInterval.Ticks + 1;
            var remainingTicks = DateTimeOffset.MaxValue.UtcTicks - activatedAt.UtcTicks;
            if (intervalCount > remainingTicks / fInterval.Ticks)
            {
                return null;
            }

            return new DateTimeOffset(activatedAt.UtcTicks + intervalCount * fInterval.Ticks, TimeSpan.Zero);
        }
    }
}
