namespace Blueprint.Log
{
    /// <summary>Defines the shared event-writing, reading, and deletion contract and captures the originating machine.</summary>
    public abstract class BlueprintLog
    {
        private readonly string fMachineName;

        /// <summary>Captures the machine name once when the log is initialized.</summary>
        protected BlueprintLog()
        {
            fMachineName = Environment.MachineName;
        }

        /// <summary>Persists an event with an automatically assigned UTC timestamp and its originating machine before returning.</summary>
        /// <param name="level">The event's log level.</param>
        /// <param name="message">The final, nonempty message.</param>
        /// <param name="logger">The optional log source, up to 500 characters.</param>
        /// <param name="exception">The optional exception associated with the event.</param>
        /// <exception cref="ArgumentException">The level is undefined, the message is empty, or the source exceeds its maximum length.</exception>
        /// <exception cref="Exception">Persistence fails and the error is propagated to the caller.</exception>
        public abstract void WriteToLog(EBlueprintLogLevel level, string message, string? logger = null, Exception? exception = null);

        /// <summary>Reads all events in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <returns>A read-only snapshot of matching events, or an empty list when none match.</returns>
        /// <exception cref="ArgumentException">A timestamp is not UTC or the interval is reversed.</exception>
        /// <exception cref="Exception">Reading or interpreting the stored events fails.</exception>
        public abstract IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo);

        /// <summary>Reads events of one level in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <returns>A read-only snapshot of matching events, or an empty list when none match.</returns>
        /// <exception cref="ArgumentException">A timestamp is not UTC, the interval is reversed, or the level is undefined.</exception>
        /// <exception cref="Exception">Reading or interpreting the stored events fails.</exception>
        public abstract IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level);

        /// <summary>Reads events of one level and machine in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <param name="machineName">The machine name to match, up to 255 characters.</param>
        /// <returns>A read-only snapshot of matching events, or an empty list when none match.</returns>
        /// <exception cref="ArgumentException">The interval, level, or machine name is invalid.</exception>
        /// <exception cref="Exception">Reading or interpreting the stored events fails.</exception>
        public abstract IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level, string machineName);

        /// <summary>Reads a page of events in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="skip">The nonnegative number of matching rows to skip.</param>
        /// <param name="take">The positive maximum number of rows to return.</param>
        /// <returns>A read-only page, or an empty list when no rows remain.</returns>
        /// <exception cref="ArgumentException">The interval or pagination arguments are invalid.</exception>
        /// <exception cref="Exception">Reading or interpreting the stored events fails.</exception>
        public abstract IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, int skip, int take);

        /// <summary>Reads a page of events of one level in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <param name="skip">The nonnegative number of matching rows to skip.</param>
        /// <param name="take">The positive maximum number of rows to return.</param>
        /// <returns>A read-only page, or an empty list when no rows remain.</returns>
        /// <exception cref="ArgumentException">The interval, level, or pagination arguments are invalid.</exception>
        /// <exception cref="Exception">Reading or interpreting the stored events fails.</exception>
        public abstract IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level, int skip, int take);

        /// <summary>Reads a page of events of one level and machine in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <param name="machineName">The machine name to match, up to 255 characters.</param>
        /// <param name="skip">The nonnegative number of matching rows to skip.</param>
        /// <param name="take">The positive maximum number of rows to return.</param>
        /// <returns>A read-only page, or an empty list when no rows remain.</returns>
        /// <exception cref="ArgumentException">The filters or pagination arguments are invalid.</exception>
        /// <exception cref="Exception">Reading or interpreting the stored events fails.</exception>
        public abstract IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level, string machineName, int skip, int take);

        /// <summary>Counts all matching events in a UTC interval before pagination.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <returns>The total number of matching events, or zero when none match.</returns>
        /// <exception cref="ArgumentException">A timestamp is not UTC or the interval is reversed.</exception>
        /// <exception cref="Exception">Counting the stored events fails.</exception>
        public abstract long CountFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo);

        /// <summary>Counts matching events of one level in a UTC interval before pagination.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <returns>The total number of matching events, or zero when none match.</returns>
        /// <exception cref="ArgumentException">The interval or level is invalid.</exception>
        /// <exception cref="Exception">Counting the stored events fails.</exception>
        public abstract long CountFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level);

        /// <summary>Counts matching events of one level and machine in a UTC interval before pagination.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <param name="machineName">The machine name to match, up to 255 characters.</param>
        /// <returns>The total number of matching events, or zero when none match.</returns>
        /// <exception cref="ArgumentException">The interval, level, or machine name is invalid.</exception>
        /// <exception cref="Exception">Counting the stored events fails.</exception>
        public abstract long CountFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level, string machineName);

        /// <summary>Deletes all events strictly before the supplied UTC timestamp, across all levels and machines.</summary>
        /// <param name="dateTimeUtcBefore">The exclusive UTC cutoff; events at or after it are retained.</param>
        /// <exception cref="ArgumentException">The cutoff does not use DateTimeKind.Utc.</exception>
        /// <exception cref="Exception">Deleting the stored events fails.</exception>
        public abstract void DeleteFromLog(DateTime dateTimeUtcBefore);

        /// <summary>The machine name captured when the log was initialized.</summary>
        public string MachineName
        {
            get
            {
                return fMachineName;
            }
        }
    }
}
