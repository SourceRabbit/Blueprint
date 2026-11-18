// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Log
{
    /// <summary>Provides an immutable snapshot of a persisted log event.</summary>
    public sealed class LogEntry
    {
        /// <summary>Captures the values read from a stored log event.</summary>
        /// <param name="id">The database-generated insertion ID.</param>
        /// <param name="logged">The stored event timestamp with DateTimeKind.Utc.</param>
        /// <param name="level">The event's log level.</param>
        /// <param name="message">The stored message.</param>
        /// <param name="logger">The optional source label.</param>
        /// <param name="exception">The optional stored exception text.</param>
        /// <param name="machineName">The originating machine name.</param>
        internal LogEntry(long id, DateTime logged, EBlueprintLogLevel level, string message, string? logger, string? exception, string machineName)
        {
            Id = id;
            Logged = logged;
            Level = level;
            Message = message;
            Logger = logger;
            Exception = exception;
            MachineName = machineName;
        }

        /// <summary>The database-generated insertion ID.</summary>
        public long Id { get; }

        /// <summary>The stored event timestamp with DateTimeKind.Utc.</summary>
        public DateTime Logged { get; }

        /// <summary>The event's log level.</summary>
        public EBlueprintLogLevel Level { get; }

        /// <summary>The stored message.</summary>
        public string Message { get; }

        /// <summary>The source label, or null when none was supplied.</summary>
        public string? Logger { get; }

        /// <summary>The stored exception text, or null; the original exception object is not reconstructed.</summary>
        public string? Exception { get; }

        /// <summary>The machine name captured by the writer.</summary>
        public string MachineName { get; }
    }
}
