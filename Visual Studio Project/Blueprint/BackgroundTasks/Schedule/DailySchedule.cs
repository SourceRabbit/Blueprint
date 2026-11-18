// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;

namespace Blueprint.BackgroundTasks.Schedule
{
    /// <summary>Schedules daily executions at specific local times.</summary>
    public sealed class DailySchedule : BackgroundTaskSchedule
    {
        private readonly ReadOnlyCollection<TimeOnly> fTimes;
        private readonly TimeZoneInfo fTimeZone;

        /// <summary>Sets the daily execution times and their time zone.</summary>
        /// <param name="times">One or more execution times. Duplicates are removed.</param>
        /// <param name="timeZone">The explicitly selected time zone.</param>
        /// <exception cref="ArgumentNullException">No times or time zone were provided.</exception>
        /// <exception cref="ArgumentException">The collection of times is empty.</exception>
        public DailySchedule(IEnumerable<TimeOnly> times, TimeZoneInfo timeZone)
        {
            ArgumentNullException.ThrowIfNull(times);
            ArgumentNullException.ThrowIfNull(timeZone);
            var orderedTimes = times.Distinct().OrderBy(time => time).ToArray();
            if (orderedTimes.Length == 0)
            {
                throw new ArgumentException("At least one daily execution time is required.", nameof(times));
            }

            fTimes = Array.AsReadOnly(orderedTimes);
            fTimeZone = timeZone;
        }

        /// <summary>The distinct execution times in ascending order.</summary>
        public IReadOnlyList<TimeOnly> Times
        {
            get
            {
                return fTimes;
            }
        }

        /// <summary>The time zone used to interpret the daily times.</summary>
        public TimeZoneInfo TimeZone
        {
            get
            {
                return fTimeZone;
            }
        }

        /// <summary>Finds the next local time, skipping nonexistent times and duplicate executions at daylight-saving transitions.</summary>
        /// <param name="after">The time after which to find an execution.</param>
        /// <param name="activatedAt">The activation time, which does not shift the fixed daily times.</param>
        /// <returns>The next execution time in UTC, or null if the date range is exhausted.</returns>
        public override DateTimeOffset? CalculateNextRunTime(DateTimeOffset after, DateTimeOffset activatedAt)
        {
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, fTimeZone).DateTime);
            while (true)
            {
                DateTimeOffset? nextRun = null;
                foreach (var time in fTimes)
                {
                    var localTime = date.ToDateTime(time, DateTimeKind.Unspecified);
                    if (fTimeZone.IsInvalidTime(localTime))
                    {
                        continue;
                    }

                    // For an ambiguous time, select only its first occurrence, which has the larger UTC offset.
                    var offset = fTimeZone.IsAmbiguousTime(localTime) ? fTimeZone.GetAmbiguousTimeOffsets(localTime).Max() : fTimeZone.GetUtcOffset(localTime);
                    var utcTicks = localTime.Ticks - offset.Ticks;
                    if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
                    {
                        continue;
                    }

                    var candidate = new DateTimeOffset(utcTicks, TimeSpan.Zero);
                    if (candidate > after && (nextRun == null || candidate < nextRun.Value))
                    {
                        nextRun = candidate;
                    }
                }

                if (nextRun != null || date == DateOnly.MaxValue)
                {
                    return nextRun;
                }

                date = date.AddDays(1);
            }
        }
    }
}
