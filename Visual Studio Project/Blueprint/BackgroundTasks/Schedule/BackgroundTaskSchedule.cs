namespace Blueprint.BackgroundTasks.Schedule
{
    /// <summary>Calculates scheduled executions of a background task.</summary>
    public abstract class BackgroundTaskSchedule
    {
        /// <summary>Finds the first execution strictly after the specified time.</summary>
        /// <param name="after">The time after which to find an execution.</param>
        /// <param name="activatedAt">The active schedule's starting point.</param>
        /// <returns>The next execution time in UTC, or null if no date is available.</returns>
        public abstract DateTimeOffset? CalculateNextRunTime(DateTimeOffset after, DateTimeOffset activatedAt);
    }
}
