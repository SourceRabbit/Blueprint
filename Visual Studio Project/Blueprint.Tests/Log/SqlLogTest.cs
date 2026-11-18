// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Log;
using Microsoft.Data.SqlClient;
using System.Transactions;

namespace Blueprint.Tests.Log
{
    /// <summary>Verifies logging through the public API in an isolated SQL Server database.</summary>
    internal sealed class SqlLogTest : BlueprintTest
    {
        /// <summary>Checks initialization, writing, reading, deletion, validation, and independence from application transactions.</summary>
        /// <exception cref="Exception">An assertion or SQL operation fails.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            CheckInvalidSettings();
            string databaseName = "BlueprintLogTest_" + Guid.NewGuid().ToString("N");
            string connectionString = Environment.GetEnvironmentVariable("BLUEPRINT_TEST_SQL_CONNECTION_STRING") ?? fDatabaseConnectionString;
            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", Pooling = false, ConnectTimeout = 5, Enlist = false };
            using (SqlConnection master = new SqlConnection(settings.ConnectionString))
            {
                master.Open();

                try
                {
                    BlueprintLog log = new SqlLog(settings.ConnectionString, databaseName, "Log");
                    settings.InitialCatalog = databaseName;
                    using (SqlConnection connection = new SqlConnection(settings.ConnectionString))
                    {
                        connection.Open();
                        CheckStoredValues(log, connection);
                        CheckInvalidEvents(log, connection);
                        CheckTransactionsAndConcurrency(log, connection, settings.ConnectionString, databaseName);
                        CheckReadEvents(connection, settings.ConnectionString, databaseName);
                        CheckDeleteEvents(connection, settings.ConnectionString, databaseName);
                    }
                }
                finally
                {
                    using (SqlCommand command = master.CreateCommand())
                    {
                        command.CommandText = $"IF DB_ID(@name) IS NOT NULL DROP DATABASE [{databaseName}];";
                        command.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 128).Value = databaseName;
                        command.ExecuteNonQuery();
                    }
                }

                Console.WriteLine($"SQL log checks passed: {getAssertionCount()} assertions.");
            }
        }

        /// <summary>Checks that invalid settings are rejected before a connection is attempted.</summary>
        /// <exception cref="InvalidOperationException">An invalid setting was accepted.</exception>
        private void CheckInvalidSettings()
        {
            const string CONNECTION_STRING = "Server=invalid;Integrated Security=True;Connect Timeout=1;";
            AssertThrows<ArgumentException>(() => new SqlLog(" ", "Logs", "Log"), "Empty connection settings must fail.");
            AssertThrows<ArgumentException>(() => new SqlLog(CONNECTION_STRING, "master", "Log"), "System databases must be rejected.");
            AssertThrows<ArgumentException>(() => new SqlLog(CONNECTION_STRING, " Logs", "Log"), "Surrounding whitespace must fail.");
            AssertThrows<ArgumentException>(() => new SqlLog(CONNECTION_STRING, "Logs", new string('x', 129)), "Overlong identifiers must fail.");
            AssertThrows<ArgumentException>(() => new SqlLog(CONNECTION_STRING, "Logs", "Log\n"), "Control characters must fail.");
            AssertThrows<ArgumentException>(() => new SqlLog("Integrated Security=True", "Logs", "Log"), "A server is required.");
        }

        /// <summary>Checks SQL-generated UTC timestamps and exact storage of Unicode, exceptions, machine names, NULL values, and all levels.</summary>
        /// <param name="log">The logger accessed through the abstract contract.</param>
        /// <param name="connection">The open connection to the temporary database.</param>
        /// <exception cref="Exception">A write or assertion fails.</exception>
        private void CheckStoredValues(BlueprintLog log, SqlConnection connection)
        {
            using (SqlCommand command = new SqlCommand("SELECT COUNT(*) FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Log') AND [name] = N'Logged' AND TYPE_NAME([user_type_id]) = N'datetime2' AND [scale] = 0 AND [is_nullable] = 0;", connection))
            {
                Assert((int)command.ExecuteScalar()! == 1, "Logged must be a nonnullable datetime2(0) column.");
            }
            using (SqlCommand command = new SqlCommand("SELECT [definition] FROM sys.default_constraints WHERE [parent_object_id] = OBJECT_ID(N'dbo.Log') AND [parent_column_id] = COLUMNPROPERTY(OBJECT_ID(N'dbo.Log'), N'Logged', 'ColumnId');", connection))
            {
                Assert(command.ExecuteScalar() is string definition && definition.Contains("sysutcdatetime", StringComparison.OrdinalIgnoreCase), "New tables must generate Logged through a SYSUTCDATETIME default constraint.");
            }

            string message = "Ενημέρωση προϊόντος '320'; DROP TABLE [dbo].[Log]; -- " + new string('Ω', 5000);
            Exception exception = CaptureException();
            DateTime beforeWrite;
            using (SqlCommand command = new SqlCommand("SELECT CAST(SYSUTCDATETIME() AS datetime2(0));", connection))
            {
                beforeWrite = (DateTime)command.ExecuteScalar()!;
            }

            log.WriteToLog(EBlueprintLogLevel.Error, message, "ProductManager.SaveProduct", exception);
            using (SqlCommand command = new SqlCommand("SELECT [Id], [Logged], [Level], [Message], [Logger], [Exception], [MachineName], CAST(SYSUTCDATETIME() AS datetime2(0)) FROM [dbo].[Log];", connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    Assert(reader.Read(), "The event must be persisted before WriteToLog returns.");
                    Assert(reader.GetInt64(0) == 1, "The database must assign the identity.");
                    DateTime logged = reader.GetDateTime(1);
                    Assert(logged >= beforeWrite && logged <= reader.GetDateTime(7), "Logged must use the SQL Server UTC clock at insertion time.");
                    Assert(logged.Ticks % TimeSpan.TicksPerSecond == 0, "Logged must retain whole-second precision.");
                    Assert(reader.GetString(2) == "Error", "The enum must be stored as its name.");
                    Assert(reader.GetString(3) == message, "Unicode, long messages and SQL-like text must survive unchanged.");
                    Assert(reader.GetString(4) == "ProductManager.SaveProduct", "The logger source must be retained.");
                    Assert(reader.GetString(5) == exception.ToString(), "The full exception including its stack and inner exception must be retained.");
                    Assert(reader.GetString(6) == log.MachineName && log.MachineName == Environment.MachineName, "The initialized machine name must be stored.");
                    Assert(!reader.Read(), "Exactly one event must be written.");
                }
            }

            foreach (EBlueprintLogLevel level in Enum.GetValues<EBlueprintLogLevel>())
            {
                log.WriteToLog(level, level.ToString());
            }

            using (SqlCommand command = new SqlCommand("SELECT [Level], [Logger], [Exception], [MachineName], [Logged], CAST(SYSUTCDATETIME() AS datetime2(0)) FROM [dbo].[Log] WHERE [Id] > 1 ORDER BY [Id];", connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    foreach (EBlueprintLogLevel level in Enum.GetValues<EBlueprintLogLevel>())
                    {
                        Assert(reader.Read() && reader.GetString(0) == level.ToString(), "Every level must be accepted and stored by name.");
                        Assert(reader.IsDBNull(1) && reader.IsDBNull(2), "Omitted optional values must be SQL NULL.");
                        Assert(reader.GetString(3) == log.MachineName, "Every row must use the initialized machine name.");
                        Assert(reader.GetDateTime(4) >= beforeWrite && reader.GetDateTime(4) <= reader.GetDateTime(5), "Every event must receive a timestamp from the SQL Server UTC clock.");
                    }
                }
            }
        }

        /// <summary>Verifies that invalid events do not leave rows in the table.</summary>
        /// <param name="log">The logger under test.</param>
        /// <param name="connection">The open verification connection.</param>
        /// <exception cref="Exception">An invalid event is accepted or the verification query fails.</exception>
        private void CheckInvalidEvents(BlueprintLog log, SqlConnection connection)
        {
            AssertThrows<ArgumentException>(() => log.WriteToLog(EBlueprintLogLevel.Info, " "), "Whitespace messages must fail.");
            AssertThrows<ArgumentOutOfRangeException>(() => log.WriteToLog((EBlueprintLogLevel)99, "Invalid level"), "Unknown levels must fail.");
            AssertThrows<ArgumentException>(() => log.WriteToLog(EBlueprintLogLevel.Info, "Long source", new string('x', 501)), "Sources must never be silently truncated.");
            using (SqlCommand command = new SqlCommand("SELECT COUNT(*) FROM [dbo].[Log];", connection))
            {
                Assert((int)command.ExecuteScalar()! == 7, "Rejected events must not write rows.");
            }
        }

        /// <summary>Checks rollback, concurrent writes, reinitialization, and safe handling of special characters in names.</summary>
        /// <param name="log">The shared logger for concurrent writes.</param>
        /// <param name="connection">The verification connection.</param>
        /// <param name="connectionString">The temporary database connection settings.</param>
        /// <param name="databaseName">The temporary database name.</param>
        /// <exception cref="Exception">A write, initialization, or assertion fails.</exception>
        private void CheckTransactionsAndConcurrency(BlueprintLog log, SqlConnection connection, string connectionString, string databaseName)
        {
            using (TransactionScope scope = new TransactionScope())
            {
                log.WriteToLog(EBlueprintLogLevel.Warn, "Survives application rollback");
            }

            Parallel.For(0, 20, index => log.WriteToLog(EBlueprintLogLevel.Info, "Concurrent " + index));
            SqlLog reinitializedLog = new SqlLog(connectionString, databaseName, "Log");
            reinitializedLog.WriteToLog(EBlueprintLogLevel.Info, "After reinitialization", new string('x', 500));
            using (SqlCommand command = new SqlCommand("SELECT COUNT(*), COUNT(DISTINCT [Id]) FROM [dbo].[Log];", connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    Assert(reader.Read() && reader.GetInt32(0) == 29 && reader.GetInt32(1) == 29, "Rollback and concurrency must not lose events; reinitialization must preserve rows.");
                }
            }

            Parallel.For(0, 4, index =>
            {
                SqlLog concurrentLog = new SqlLog(connectionString, databaseName, "ConcurrentLog");
                concurrentLog.WriteToLog(EBlueprintLogLevel.Info, "Initializer " + index);
            });
            using (SqlCommand command = new SqlCommand("SELECT COUNT(*) FROM [dbo].[ConcurrentLog];", connection))
            {
                Assert((int)command.ExecuteScalar()! == 4, "Concurrent table initialization must succeed for every caller.");
            }

            SqlLog quotedLog = new SqlLog(connectionString, databaseName, "Log]; DROP TABLE Log;--");
            quotedLog.WriteToLog(EBlueprintLogLevel.Info, "Quoted identifier");
            using (SqlCommand command = new SqlCommand("SELECT COUNT(*) FROM [dbo].[Log]]; DROP TABLE Log;--];", connection))
            {
                Assert((int)command.ExecuteScalar()! == 1, "Table names must be delimited as identifiers.");
            }
            using (SqlCommand command = new SqlCommand("SELECT COUNT(*) FROM [dbo].[Log];", connection))
            {
                Assert((int)command.ExecuteScalar()! == 29, "Identifier-like SQL must not affect the original table.");
            }
        }

        /// <summary>Checks reads, counts, pagination, exact boundaries, stored values, snapshots, and concurrent reads.</summary>
        /// <param name="connection">The open connection to the temporary database.</param>
        /// <param name="connectionString">The temporary database connection settings.</param>
        /// <param name="databaseName">The temporary database name.</param>
        /// <exception cref="Exception">Setup, reading, or an assertion fails.</exception>
        private void CheckReadEvents(SqlConnection connection, string connectionString, string databaseName)
        {
            BlueprintLog log = new SqlLog(connectionString, databaseName, "ReadLog");
            DateTime fromUtc = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
            DateTime toUtc = fromUtc.AddDays(1);
            string message = "Unicode Ω and 'quotes'; -- " + new string('Ω', 5000);
            Exception exception = CaptureException();
            log.WriteToLog(EBlueprintLogLevel.Error, "Before the interval");
            log.WriteToLog(EBlueprintLogLevel.Error, "At the excluded end");
            log.WriteToLog(EBlueprintLogLevel.Error, message, "ProductImport", exception);
            log.WriteToLog(EBlueprintLogLevel.Info, "Late event");
            log.WriteToLog(EBlueprintLogLevel.Error, "Earlier event inserted later");
            log.WriteToLog(EBlueprintLogLevel.Fatal, "Fatal event");
            log.WriteToLog(EBlueprintLogLevel.Error, "Other machine");

            // Fix persisted timestamps for deterministic boundary tests and simulate one event from another host.
            string otherMachine = "OtherMachine_'Ω";
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    UPDATE [dbo].[ReadLog]
                    SET [Logged] = CASE [Id]
                        WHEN 1 THEN DATEADD(second, -1, @from)
                        WHEN 2 THEN @to
                        WHEN 3 THEN @from
                        WHEN 4 THEN DATEADD(second, -1, @to)
                        WHEN 5 THEN DATEADD(hour, 1, @from)
                        WHEN 6 THEN DATEADD(second, 1, @from)
                        WHEN 7 THEN DATEADD(second, 2, @from)
                        ELSE [Logged] END,
                        [MachineName] = CASE WHEN [Id] = 7 THEN @machineName ELSE [MachineName] END;
                    """;
                command.Parameters.Add("@from", System.Data.SqlDbType.DateTime2).Value = fromUtc;
                command.Parameters.Add("@to", System.Data.SqlDbType.DateTime2).Value = toUtc;
                command.Parameters.Add("@machineName", System.Data.SqlDbType.NVarChar, 255).Value = otherMachine;
                command.ExecuteNonQuery();
            }

            IReadOnlyList<LogEntry> entries = log.ReadFromLog(fromUtc, toUtc);
            Assert(entries.Count == 5, "The start must be included and the end excluded across all levels and machines.");
            Assert(entries.Select(entry => entry.Id).SequenceEqual(new long[] { 7, 6, 5, 4, 3 }), "Results must use descending insertion IDs even when event timestamps are out of order.");
            Assert(entries.All(entry => entry.Logged.Kind == DateTimeKind.Utc), "Every timestamp must be returned as UTC.");
            LogEntry stored = entries.Single(entry => entry.Id == 3);
            Assert(stored.Logged == fromUtc && stored.Level == EBlueprintLogLevel.Error, "The timestamp and enum must be mapped correctly.");
            Assert(stored.Message == message && stored.Logger == "ProductImport", "Message and logger text must round-trip without truncation.");
            Assert(stored.Exception == exception.ToString(), "Exception text must round-trip without reconstructing an exception object.");
            Assert(stored.MachineName == log.MachineName, "The stored machine name must be returned.");
            Assert(entries[0].MachineName == otherMachine, "Unfiltered reads must include other machines.");
            Assert(entries[0].Logger == null && entries[0].Exception == null, "SQL NULL values must remain null.");

            IReadOnlyList<LogEntry> errors = log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error);
            Assert(errors.Select(entry => entry.Id).SequenceEqual(new long[] { 7, 5, 3 }), "Level filtering must select exactly Error and preserve ID order.");
            IReadOnlyList<LogEntry> localErrors = log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, log.MachineName);
            Assert(localErrors.Select(entry => entry.Id).SequenceEqual(new long[] { 5, 3 }), "Machine and level filters must be applied together.");
            IReadOnlyList<LogEntry> remoteErrors = log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, otherMachine);
            Assert(remoteErrors.Count == 1 && remoteErrors[0].Id == 7, "A supplied machine name with Unicode and quotes must be matched safely.");

            Assert(log.ReadFromLog(fromUtc.AddDays(2), toUtc.AddDays(2)).Count == 0, "An interval without events must return an empty list.");
            Assert(log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Debug).Count == 0, "An unmatched level must return an empty list.");
            Assert(log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, "Missing' OR 1=1;--").Count == 0, "The machine filter must remain a SQL parameter.");
            Assert(log.ReadFromLog(fromUtc, fromUtc).Count == 0, "An equal-bound interval must be empty.");
            Assert(log.ReadFromLog(fromUtc.AddTicks(1), fromUtc.AddSeconds(1)).Count == 0, "A fractional lower bound must not round down and include the start event.");
            IReadOnlyList<LogEntry> fractionalInterval = log.ReadFromLog(fromUtc, fromUtc.AddTicks(1));
            Assert(fractionalInterval.Count == 1 && fractionalInterval[0].Id == 3, "A fractional upper bound must preserve an included whole-second event.");

            Task<IReadOnlyList<LogEntry>>[] reads = Enumerable.Range(0, 4).Select(index => Task.Run(() => log.ReadFromLog(fromUtc, toUtc))).ToArray();
            foreach (IReadOnlyList<LogEntry> result in Task.WhenAll(reads).GetAwaiter().GetResult())
            {
                Assert(result.Count == 5, "Concurrent reads must use independent SQL resources.");
            }

            CheckInvalidReadFilters(log, fromUtc, toUtc);
            CheckCountsAndPages(log, fromUtc, toUtc, otherMachine);
            CheckInvalidPaginationAndCountFilters(log, fromUtc, toUtc);
            for (int index = 0; index < 105; index++)
            {
                log.WriteToLog(EBlueprintLogLevel.Debug, "Additional event " + index);
            }
            using (SqlCommand command = new SqlCommand("UPDATE [dbo].[ReadLog] SET [Logged] = @logged WHERE [Id] > 7;", connection))
            {
                command.Parameters.Add("@logged", System.Data.SqlDbType.DateTime2).Value = fromUtc.AddMinutes(5);
                command.ExecuteNonQuery();
            }
            Assert(log.ReadFromLog(fromUtc, toUtc).Count == 110, "Reads must return all matches without an implicit 100-row limit.");
            Assert(log.CountFromLog(fromUtc, toUtc) == 110L, "Counts must include all matching rows, not just one page.");
            Assert(log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Debug) == 105L, "Counts must apply the level filter before pagination.");
            List<long> pagedIds = new List<long>();
            for (int skip = 0; skip < 110; skip += 25)
            {
                IReadOnlyList<LogEntry> page = log.ReadFromLog(fromUtc, toUtc, skip, 25);
                Assert(page.Count == Math.Min(25, 110 - skip), "Pages must respect the requested size, including a partial last page.");
                pagedIds.AddRange(page.Select(entry => entry.Id));
            }
            Assert(pagedIds.SequenceEqual(log.ReadFromLog(fromUtc, toUtc).Select(entry => entry.Id)), "Paging an unchanged result set must return every row once in the same order as an unpaged read.");
            Assert(entries.Count == 5, "Previously returned snapshots must not change after later writes.");
            if (entries is ICollection<LogEntry> collection)
            {
                Assert(collection.IsReadOnly, "A returned snapshot must not expose a writable collection.");
            }

            BlueprintLog reopenedLog = new SqlLog(connectionString, databaseName, "ReadLog");
            Assert(reopenedLog.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, otherMachine).Count == 1, "A new logger instance must read previously persisted events.");

            using (SqlCommand command = new SqlCommand("UPDATE [dbo].[ReadLog] SET [Level] = 'Unknown' WHERE [Id] = 3;", connection))
            {
                command.ExecuteNonQuery();
            }
            AssertThrows<InvalidOperationException>(() => log.ReadFromLog(fromUtc, toUtc), "Unknown stored levels must fail explicitly.");
            Assert(log.CountFromLog(fromUtc, toUtc) == 110L, "Counting must not materialize or interpret event contents.");
            Assert(log.ReadFromLog(fromUtc, toUtc, 0, 1).Count == 1, "A page must not load an invalid event outside the requested SQL range.");
            AssertThrows<InvalidOperationException>(() => log.ReadFromLog(fromUtc, toUtc, 109, 1), "An invalid event inside the selected page must still fail explicitly.");
            using (SqlCommand command = new SqlCommand("DROP TABLE [dbo].[ReadLog];", connection))
            {
                command.ExecuteNonQuery();
            }
            AssertThrows<SqlException>(() => log.ReadFromLog(fromUtc, toUtc), "Database failures must propagate instead of returning an empty list.");
            AssertThrows<SqlException>(() => log.ReadFromLog(fromUtc, toUtc, 0, 25), "Paged reads must propagate database failures.");
            AssertThrows<SqlException>(() => log.CountFromLog(fromUtc, toUtc), "Count failures must propagate instead of returning zero.");
        }

        /// <summary>Checks UTC deletion boundaries, all levels and machines, transaction independence, and continued logging.</summary>
        /// <param name="connection">The open verification connection to the temporary database.</param>
        /// <param name="connectionString">The temporary database connection settings.</param>
        /// <param name="databaseName">The isolated database name.</param>
        /// <exception cref="Exception">Setup, deletion, or an assertion fails.</exception>
        private void CheckDeleteEvents(SqlConnection connection, string connectionString, string databaseName)
        {
            BlueprintLog log = new SqlLog(connectionString, databaseName, "DeleteLog");
            DateTime cutoffUtc = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            DateTime minimumUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
            DateTime maximumUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
            EBlueprintLogLevel[] levels = Enum.GetValues<EBlueprintLogLevel>();
            foreach (EBlueprintLogLevel level in levels)
            {
                log.WriteToLog(level, "Old " + level);
            }
            log.WriteToLog(EBlueprintLogLevel.Info, "At the cutoff");
            log.WriteToLog(EBlueprintLogLevel.Warn, "After the cutoff");

            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    UPDATE [dbo].[DeleteLog]
                    SET [Logged] = CASE
                        WHEN [Id] <= @oldCount THEN DATEADD(second, -1, @cutoff)
                        WHEN [Id] = @oldCount + 1 THEN @cutoff
                        ELSE DATEADD(second, 1, @cutoff) END,
                        [MachineName] = CASE WHEN [Id] % 2 = 0 THEN N'OtherMachine' ELSE [MachineName] END;
                    """;
                command.Parameters.Add("@oldCount", System.Data.SqlDbType.Int).Value = levels.Length;
                SqlParameter cutoff = command.Parameters.Add("@cutoff", System.Data.SqlDbType.DateTime2);
                cutoff.Scale = 7;
                cutoff.Value = cutoffUtc;
                command.ExecuteNonQuery();
            }

            IReadOnlyList<LogEntry> originalEntries = log.ReadFromLog(minimumUtc, maximumUtc);
            Assert(originalEntries.Count == levels.Length + 2, "Deletion checks must begin with all old events and both retained events.");
            AssertThrows<ArgumentException>(() => log.DeleteFromLog(DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Local)), "Deletion must reject a local cutoff.");
            AssertThrows<ArgumentException>(() => log.DeleteFromLog(DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Unspecified)), "Deletion must reject an unspecified cutoff.");
            Assert(log.CountFromLog(minimumUtc, maximumUtc) == originalEntries.Count, "Rejected deletion arguments must leave all rows unchanged.");

            log.DeleteFromLog(minimumUtc);
            Assert(log.CountFromLog(minimumUtc, maximumUtc) == originalEntries.Count, "A cutoff before every event must delete nothing.");
            using (TransactionScope scope = new TransactionScope())
            {
                log.DeleteFromLog(cutoffUtc);
            }

            IReadOnlyList<LogEntry> remainingEntries = log.ReadFromLog(minimumUtc, maximumUtc);
            Assert(remainingEntries.Select(entry => entry.Message).SequenceEqual(new string[] { "After the cutoff", "At the cutoff" }), "Deletion must remove all older levels and machines while retaining the exact boundary and later events, even after ambient rollback.");
            Assert(originalEntries.Count == levels.Length + 2, "Deleting stored events must not change previously returned snapshots.");
            log.DeleteFromLog(cutoffUtc);
            Assert(log.CountFromLog(minimumUtc, maximumUtc) == 2, "Repeating the same deletion must leave retained events unchanged.");

            log.DeleteFromLog(cutoffUtc.AddTicks(1));
            Assert(log.ReadFromLog(minimumUtc, maximumUtc).Single().Message == "After the cutoff", "A fractional cutoff must delete the earlier whole-second event without rounding the cutoff down.");
            log.DeleteFromLog(maximumUtc);
            log.DeleteFromLog(maximumUtc);
            Assert(log.CountFromLog(minimumUtc, maximumUtc) == 0, "Deleting every event and then deleting from an empty log must succeed.");

            log.WriteToLog(EBlueprintLogLevel.Info, "Logging after cleanup");
            Assert(log.ReadFromLog(minimumUtc, maximumUtc).Single().Message == "Logging after cleanup", "The log must remain usable after deleting all events.");
            using (SqlCommand command = new SqlCommand("SELECT COUNT(*) FROM [dbo].[Log];", connection))
            {
                Assert((int)command.ExecuteScalar()! == 29, "Deletion must affect only the configured log table.");
            }
            using (SqlCommand command = new SqlCommand("DROP TABLE [dbo].[DeleteLog];", connection))
            {
                command.ExecuteNonQuery();
            }
            AssertThrows<SqlException>(() => log.DeleteFromLog(cutoffUtc), "SQL deletion failures must propagate to the caller.");
            AssertThrows<ArgumentException>(() => log.DeleteFromLog(DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Local)), "Invalid cutoffs must be rejected before executing SQL against a missing table.");
        }

        /// <summary>Verifies matching count filters and SQL paging against a fixed set of known events.</summary>
        /// <param name="log">The logger with five matching events across levels and machines.</param>
        /// <param name="fromUtc">The inclusive UTC boundary.</param>
        /// <param name="toUtc">The exclusive UTC boundary.</param>
        /// <param name="otherMachine">The machine name stored on the latest event.</param>
        /// <exception cref="Exception">A read, count, or assertion fails.</exception>
        private void CheckCountsAndPages(BlueprintLog log, DateTime fromUtc, DateTime toUtc, string otherMachine)
        {
            Assert(log.CountFromLog(fromUtc, toUtc) == 5L, "Unfiltered counts must include all matching levels and machines with the same interval boundaries as reads.");
            Assert(log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error) == 3L, "Counts must match the exact selected level.");
            Assert(log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, log.MachineName) == 2L, "Counts must apply level and machine filters together.");
            Assert(log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, otherMachine) == 1L, "Count machine parameters must preserve Unicode and quotes.");
            Assert(log.CountFromLog(fromUtc.AddDays(2), toUtc.AddDays(2)) == 0L, "Intervals with no events must have a zero count.");
            Assert(log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Debug) == 0L, "An unmatched level must have a zero count.");
            Assert(log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, "Missing' OR 1=1;--") == 0L, "Count machine filters must remain SQL parameters.");
            Assert(log.CountFromLog(fromUtc, fromUtc) == 0L, "An equal-bound interval must have a zero count.");
            Assert(log.CountFromLog(fromUtc.AddTicks(1), fromUtc.AddSeconds(1)) == 0L, "Counts must preserve fractional lower bounds.");
            Assert(log.CountFromLog(fromUtc, fromUtc.AddTicks(1)) == 1L, "Counts must preserve fractional upper bounds.");

            IReadOnlyList<LogEntry> firstPage = log.ReadFromLog(fromUtc, toUtc, 0, 2);
            Assert(firstPage.Select(entry => entry.Id).SequenceEqual(new long[] { 7, 6 }), "The first page must use descending insertion IDs.");
            Assert(log.ReadFromLog(fromUtc, toUtc, 2, 2).Select(entry => entry.Id).SequenceEqual(new long[] { 5, 4 }), "The next page must skip the previous matching rows.");
            Assert(log.ReadFromLog(fromUtc, toUtc, 4, 2).Single().Id == 3, "A partial final page must include the remaining event.");
            Assert(log.ReadFromLog(fromUtc, toUtc, 5, 2).Count == 0, "Skipping all matches must return an empty page.");
            Assert(log.ReadFromLog(fromUtc, toUtc, int.MaxValue, 2).Count == 0, "Large valid offsets must not overflow or repeat rows.");
            Assert(log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, 1, 1).Single().Id == 5, "Level filtering must precede SQL pagination.");
            Assert(log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, log.MachineName, 1, 25).Single().Id == 3, "Machine filtering must precede SQL pagination.");
            Assert(log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, otherMachine, 0, 1).Single().Id == 7, "Paged machine filters must preserve Unicode and quotes.");
            Assert(log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, "Missing' OR 1=1;--", 0, 25).Count == 0, "Paged machine filters must remain SQL parameters.");
            Assert(log.ReadFromLog(fromUtc, fromUtc, 0, 25).Count == 0, "An equal-bound interval must produce an empty page.");
            Assert(log.ReadFromLog(fromUtc.AddTicks(1), fromUtc.AddSeconds(1), 0, 25).Count == 0, "Paged reads must preserve fractional lower bounds.");
            Assert(log.ReadFromLog(fromUtc, fromUtc.AddTicks(1), 0, 25).Single().Id == 3, "Paged reads must preserve fractional upper bounds.");
            Assert(firstPage is ICollection<LogEntry> collection && collection.IsReadOnly, "Pages must remain read-only snapshots.");
        }

        /// <summary>Verifies that counting and paged reads reject invalid filters and pagination arguments.</summary>
        /// <param name="log">The logger under test.</param>
        /// <param name="fromUtc">A valid inclusive UTC boundary.</param>
        /// <param name="toUtc">A valid exclusive UTC boundary.</param>
        /// <exception cref="InvalidOperationException">An invalid argument is accepted.</exception>
        private void CheckInvalidPaginationAndCountFilters(BlueprintLog log, DateTime fromUtc, DateTime toUtc)
        {
            AssertThrows<ArgumentOutOfRangeException>(() => log.ReadFromLog(fromUtc, toUtc, -1, 25), "Negative offsets must fail.");
            AssertThrows<ArgumentOutOfRangeException>(() => log.ReadFromLog(fromUtc, toUtc, 0, 0), "A zero page size must fail.");
            AssertThrows<ArgumentOutOfRangeException>(() => log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, 0, -1), "Level-filtered pages must reject negative sizes.");
            AssertThrows<ArgumentOutOfRangeException>(() => log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, log.MachineName, -1, 25), "Machine-filtered pages must reject negative offsets.");
            AssertThrows<ArgumentException>(() => log.ReadFromLog(DateTime.SpecifyKind(fromUtc, DateTimeKind.Local), toUtc, 0, 25), "Paged reads must require UTC boundaries.");
            AssertThrows<ArgumentException>(() => log.ReadFromLog(toUtc, fromUtc, 0, 25), "Paged reads must reject reversed intervals.");
            AssertThrows<ArgumentOutOfRangeException>(() => log.ReadFromLog(fromUtc, toUtc, (EBlueprintLogLevel)99, 0, 25), "Paged reads must reject undefined levels.");
            AssertThrows<ArgumentNullException>(() => log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, null!, 0, 25), "Paged reads must reject null machine names.");
            AssertThrows<ArgumentException>(() => log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, new string('x', 256), 0, 25), "Paged reads must reject overlong machine names.");
            AssertThrows<ArgumentException>(() => log.CountFromLog(DateTime.SpecifyKind(fromUtc, DateTimeKind.Local), toUtc), "Counts must require a UTC lower boundary.");
            AssertThrows<ArgumentException>(() => log.CountFromLog(fromUtc, DateTime.SpecifyKind(toUtc, DateTimeKind.Unspecified), EBlueprintLogLevel.Error), "Counts must require a UTC upper boundary.");
            AssertThrows<ArgumentException>(() => log.CountFromLog(toUtc, fromUtc, EBlueprintLogLevel.Error, log.MachineName), "Counts must reject reversed intervals.");
            AssertThrows<ArgumentOutOfRangeException>(() => log.CountFromLog(fromUtc, toUtc, (EBlueprintLogLevel)99), "Counts must reject undefined levels.");
            AssertThrows<ArgumentNullException>(() => log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, null!), "Counts must reject null machine names.");
            AssertThrows<ArgumentException>(() => log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, " "), "Counts must reject whitespace-only machine names.");
            AssertThrows<ArgumentException>(() => log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, new string('x', 256)), "Counts must reject overlong machine names.");
        }

        /// <summary>Checks UTC, interval order, level, and machine-name validation for the read overloads.</summary>
        /// <param name="log">The logger under test.</param>
        /// <param name="fromUtc">A valid inclusive UTC boundary.</param>
        /// <param name="toUtc">A valid exclusive UTC boundary.</param>
        /// <exception cref="InvalidOperationException">An invalid filter is accepted.</exception>
        private void CheckInvalidReadFilters(BlueprintLog log, DateTime fromUtc, DateTime toUtc)
        {
            AssertThrows<ArgumentException>(() => log.ReadFromLog(DateTime.SpecifyKind(fromUtc, DateTimeKind.Local), toUtc), "Local lower bounds must fail.");
            AssertThrows<ArgumentException>(() => log.ReadFromLog(fromUtc, DateTime.SpecifyKind(toUtc, DateTimeKind.Unspecified)), "Unspecified upper bounds must fail.");
            AssertThrows<ArgumentException>(() => log.ReadFromLog(toUtc, fromUtc), "Reversed intervals must fail.");
            AssertThrows<ArgumentException>(() => log.ReadFromLog(fromUtc, DateTime.SpecifyKind(toUtc, DateTimeKind.Local), EBlueprintLogLevel.Error), "Level-filtered reads must validate UTC boundaries.");
            AssertThrows<ArgumentException>(() => log.ReadFromLog(toUtc, fromUtc, EBlueprintLogLevel.Error, log.MachineName), "Machine-filtered reads must validate interval order.");
            AssertThrows<ArgumentOutOfRangeException>(() => log.ReadFromLog(fromUtc, toUtc, (EBlueprintLogLevel)99), "Undefined read levels must fail.");
            AssertThrows<ArgumentOutOfRangeException>(() => log.ReadFromLog(fromUtc, toUtc, (EBlueprintLogLevel)99, log.MachineName), "Machine-filtered reads must reject undefined levels.");
            AssertThrows<ArgumentNullException>(() => log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, null!), "Null machine names must fail.");
            AssertThrows<ArgumentException>(() => log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, " "), "Whitespace-only machine names must fail.");
            AssertThrows<ArgumentException>(() => log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, new string('x', 256)), "Overlong machine names must fail without silent truncation.");
        }

        /// <summary>Creates an exception with a real stack trace and inner exception for persistence checks.</summary>
        /// <returns>The captured exception.</returns>
        private static Exception CaptureException()
        {
            try
            {
                throw new InvalidOperationException("Αποτυχία ενημέρωσης", new ArgumentException("Invalid product"));
            }
            catch (InvalidOperationException exception)
            {
                return exception;
            }
        }
    }
}
