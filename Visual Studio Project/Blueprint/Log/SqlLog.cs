using Microsoft.Data.SqlClient;
using System.Data;

namespace Blueprint.Log
{
    /// <summary>Writes, reads, and deletes events in a dedicated SQL Server table using parameterized commands.</summary>
    public sealed class SqlLog : BlueprintLog
    {
        private readonly string fConnectionString;
        private readonly string fTableName;

        /// <summary>Initializes the connection settings and creates the database and table only when missing.</summary>
        /// <param name="sqlConnectionString">The SQL Server connection settings.</param>
        /// <param name="databaseName">The database name, which overrides the name in the connection string.</param>
        /// <param name="tableName">The unqualified table name in the dbo schema, without SQL quoting.</param>
        /// <exception cref="ArgumentException">The connection settings or names are invalid.</exception>
        /// <exception cref="SqlException">Connecting to or creating the database or table fails.</exception>
        public SqlLog(string sqlConnectionString, string databaseName, string tableName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sqlConnectionString);
            ValidateIdentifier(databaseName, nameof(databaseName));
            ValidateIdentifier(tableName, nameof(tableName));

            if (new[] { "master", "model", "msdb", "tempdb" }.Contains(databaseName, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A system database cannot be used as the log database.", nameof(databaseName));
            }

            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(sqlConnectionString)
            {
                InitialCatalog = databaseName,
                Enlist = false
            };

            if (string.IsNullOrWhiteSpace(settings.DataSource) || !string.IsNullOrEmpty(settings.AttachDBFilename))
            {
                throw new ArgumentException("Specify Server without AttachDBFilename.", nameof(sqlConnectionString));
            }

            fConnectionString = settings.ConnectionString;
            fTableName = "[dbo].[" + tableName.Replace("]", "]]") + "]";
            InitializeDatabase(databaseName);
            InitializeTable();
        }

        /// <summary>Synchronously writes an event using the SQL Server UTC clock, independently of any application transaction.</summary>
        /// <param name="level">The log level, stored by name.</param>
        /// <param name="message">The final, nonempty message.</param>
        /// <param name="logger">The optional log source, up to 500 characters.</param>
        /// <param name="exception">The optional exception, stored using ToString().</param>
        /// <exception cref="ArgumentException">An event value is invalid.</exception>
        /// <exception cref="SqlException">Connecting to or writing to the database fails.</exception>
        public override void WriteToLog(EBlueprintLogLevel level, string message, string? logger = null, Exception? exception = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            if (!Enum.IsDefined(level))
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "The log level is not defined.");
            }

            if (logger?.Length > 500)
            {
                throw new ArgumentException("The logger name cannot exceed 500 characters.", nameof(logger));
            }

            using (SqlConnection connection = new SqlConnection(fConnectionString))
            {
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"INSERT INTO {fTableName} ([Level], [Message], [Logger], [Exception], [MachineName]) VALUES (@level, @message, @logger, @exception, @machineName);";
                    command.Parameters.Add("@level", SqlDbType.VarChar, 10).Value = level.ToString();
                    command.Parameters.Add("@message", SqlDbType.NVarChar, -1).Value = message;
                    command.Parameters.Add("@logger", SqlDbType.NVarChar, 500).Value = (object?)logger ?? DBNull.Value;
                    command.Parameters.Add("@exception", SqlDbType.NVarChar, -1).Value = (object?)exception?.ToString() ?? DBNull.Value;
                    command.Parameters.Add("@machineName", SqlDbType.NVarChar, 255).Value = MachineName;
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Reads all events in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <returns>A read-only snapshot of matching events, or an empty list when none match.</returns>
        /// <exception cref="ArgumentException">A timestamp is not UTC or the interval is reversed.</exception>
        /// <exception cref="InvalidOperationException">A stored log level is not recognized.</exception>
        /// <exception cref="SqlException">Connecting to or reading from the database fails.</exception>
        public override IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo)
        {
            return ReadEntries(dateTimeUtcFrom, dateTimeUtcTo, null, null);
        }

        /// <summary>Reads events of one level in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <returns>A read-only snapshot of matching events, or an empty list when none match.</returns>
        /// <exception cref="ArgumentException">A timestamp is not UTC, the interval is reversed, or the level is undefined.</exception>
        /// <exception cref="InvalidOperationException">A stored log level is not recognized.</exception>
        /// <exception cref="SqlException">Connecting to or reading from the database fails.</exception>
        public override IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level)
        {
            return ReadEntries(dateTimeUtcFrom, dateTimeUtcTo, level, null);
        }

        /// <summary>Reads events of one level and machine in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <param name="machineName">The machine name to match using the database collation, up to 255 characters.</param>
        /// <returns>A read-only snapshot of matching events, or an empty list when none match.</returns>
        /// <exception cref="ArgumentException">The interval, level, or machine name is invalid.</exception>
        /// <exception cref="InvalidOperationException">A stored log level is not recognized.</exception>
        /// <exception cref="SqlException">Connecting to or reading from the database fails.</exception>
        public override IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level, string machineName)
        {
            ValidateMachineName(machineName);
            return ReadEntries(dateTimeUtcFrom, dateTimeUtcTo, level, machineName);
        }

        /// <summary>Reads a SQL-limited page of events in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="skip">The nonnegative number of matching rows to skip.</param>
        /// <param name="take">The positive maximum number of rows to return.</param>
        /// <returns>A read-only page, or an empty list when no rows remain.</returns>
        /// <exception cref="ArgumentException">The interval or pagination arguments are invalid.</exception>
        /// <exception cref="InvalidOperationException">A stored log level in the page is not recognized.</exception>
        /// <exception cref="SqlException">Connecting to or reading from the database fails.</exception>
        public override IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, int skip, int take)
        {
            return ReadEntries(dateTimeUtcFrom, dateTimeUtcTo, null, null, skip, take);
        }

        /// <summary>Reads a SQL-limited page of one log level in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <param name="skip">The nonnegative number of matching rows to skip.</param>
        /// <param name="take">The positive maximum number of rows to return.</param>
        /// <returns>A read-only page, or an empty list when no rows remain.</returns>
        /// <exception cref="ArgumentException">The interval, level, or pagination arguments are invalid.</exception>
        /// <exception cref="InvalidOperationException">A stored log level in the page is not recognized.</exception>
        /// <exception cref="SqlException">Connecting to or reading from the database fails.</exception>
        public override IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level, int skip, int take)
        {
            return ReadEntries(dateTimeUtcFrom, dateTimeUtcTo, level, null, skip, take);
        }

        /// <summary>Reads a SQL-limited page for one level and machine in a UTC interval, ordered by descending insertion ID.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <param name="machineName">The machine name to match using the database collation, up to 255 characters.</param>
        /// <param name="skip">The nonnegative number of matching rows to skip.</param>
        /// <param name="take">The positive maximum number of rows to return.</param>
        /// <returns>A read-only page, or an empty list when no rows remain.</returns>
        /// <exception cref="ArgumentException">The filters or pagination arguments are invalid.</exception>
        /// <exception cref="InvalidOperationException">A stored log level in the page is not recognized.</exception>
        /// <exception cref="SqlException">Connecting to or reading from the database fails.</exception>
        public override IReadOnlyList<LogEntry> ReadFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level, string machineName, int skip, int take)
        {
            ValidateMachineName(machineName);
            return ReadEntries(dateTimeUtcFrom, dateTimeUtcTo, level, machineName, skip, take);
        }

        /// <summary>Counts matching events directly in SQL without loading their contents.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <returns>The total number of matching events before pagination.</returns>
        /// <exception cref="ArgumentException">A timestamp is not UTC or the interval is reversed.</exception>
        /// <exception cref="SqlException">Connecting to or counting rows in the database fails.</exception>
        public override long CountFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo)
        {
            return CountEntries(dateTimeUtcFrom, dateTimeUtcTo, null, null);
        }

        /// <summary>Counts events of one level directly in SQL without loading their contents.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <returns>The total number of matching events before pagination.</returns>
        /// <exception cref="ArgumentException">The interval or level is invalid.</exception>
        /// <exception cref="SqlException">Connecting to or counting rows in the database fails.</exception>
        public override long CountFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level)
        {
            return CountEntries(dateTimeUtcFrom, dateTimeUtcTo, level, null);
        }

        /// <summary>Counts events of one level and machine directly in SQL without loading their contents.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact log level to include.</param>
        /// <param name="machineName">The machine name to match using the database collation, up to 255 characters.</param>
        /// <returns>The total number of matching events before pagination.</returns>
        /// <exception cref="ArgumentException">The interval, level, or machine name is invalid.</exception>
        /// <exception cref="SqlException">Connecting to or counting rows in the database fails.</exception>
        public override long CountFromLog(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel level, string machineName)
        {
            ValidateMachineName(machineName);
            return CountEntries(dateTimeUtcFrom, dateTimeUtcTo, level, machineName);
        }

        /// <summary>Synchronously deletes all events strictly before a UTC cutoff, independently of any application transaction.</summary>
        /// <param name="dateTimeUtcBefore">The exclusive UTC cutoff, including any fractional seconds.</param>
        /// <exception cref="ArgumentException">The cutoff does not use DateTimeKind.Utc.</exception>
        /// <exception cref="SqlException">Connecting to or deleting from the database fails.</exception>
        public override void DeleteFromLog(DateTime dateTimeUtcBefore)
        {
            if (dateTimeUtcBefore.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The deletion cutoff must use DateTimeKind.Utc.", nameof(dateTimeUtcBefore));
            }

            using (SqlConnection connection = new SqlConnection(fConnectionString))
            {
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"DELETE FROM {fTableName} WHERE [Logged] < @before;";
                    SqlParameter before = command.Parameters.Add("@before", SqlDbType.DateTime2);
                    before.Scale = 7;
                    before.Value = dateTimeUtcBefore;
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Rejects invalid machine filters before they can be truncated or treated as absent.</summary>
        /// <param name="machineName">The required machine name, up to 255 characters.</param>
        /// <exception cref="ArgumentException">The name is null, empty, whitespace-only, or too long.</exception>
        private static void ValidateMachineName(string machineName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

            if (machineName.Length > 255)
            {
                throw new ArgumentException("The machine name cannot exceed 255 characters.", nameof(machineName));
            }
        }

        /// <summary>Materializes matching rows, optionally limited to a SQL page, before releasing the SQL resources.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact level, or null to include all levels.</param>
        /// <param name="machineName">The validated machine name, or null to include all machines.</param>
        /// <param name="skip">The nonnegative number of matching rows to skip when take is supplied.</param>
        /// <param name="take">The positive page size, or null to return all matching rows.</param>
        /// <returns>A read-only snapshot ordered by descending insertion ID.</returns>
        /// <exception cref="ArgumentException">The interval, level, or pagination arguments are invalid.</exception>
        /// <exception cref="InvalidOperationException">A stored log level is not recognized.</exception>
        /// <exception cref="SqlException">Connecting to or reading from the database fails.</exception>
        private IReadOnlyList<LogEntry> ReadEntries(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel? level, string? machineName, int skip = 0, int? take = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(skip);
            if (take.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take.Value, nameof(take));
            }

            List<LogEntry> entries = new List<LogEntry>();
            using (SqlConnection connection = new SqlConnection(fConnectionString))
            {
                using (SqlCommand command = connection.CreateCommand())
                {
                    ConfigureReadCommand(command, dateTimeUtcFrom, dateTimeUtcTo, level, machineName, false);
                    command.CommandText += " ORDER BY [Id] DESC";
                    if (take.HasValue)
                    {
                        command.CommandText += " OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
                        command.Parameters.Add("@skip", SqlDbType.Int).Value = skip;
                        command.Parameters.Add("@take", SqlDbType.Int).Value = take.Value;
                    }

                    command.CommandText += ";";
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string storedLevel = reader.GetString(2);
                            if (!Enum.TryParse(storedLevel, out EBlueprintLogLevel entryLevel) || !Enum.IsDefined(entryLevel))
                            {
                                throw new InvalidOperationException($"The stored log level '{storedLevel}' is not defined.");
                            }

                            DateTime logged = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
                            string? logger = reader.IsDBNull(4) ? null : reader.GetString(4);
                            string? exception = reader.IsDBNull(5) ? null : reader.GetString(5);
                            entries.Add(new LogEntry(reader.GetInt64(0), logged, entryLevel, reader.GetString(3), logger, exception, reader.GetString(6)));
                        }
                    }
                }
            }

            return entries.AsReadOnly();
        }

        /// <summary>Executes a SQL count using the same filters as event reads without materializing log entries.</summary>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact level, or null to include all levels.</param>
        /// <param name="machineName">The validated machine name, or null to include all machines.</param>
        /// <returns>The number of matching rows as a 64-bit integer.</returns>
        /// <exception cref="ArgumentException">The interval or level is invalid.</exception>
        /// <exception cref="SqlException">Connecting to or counting rows in the database fails.</exception>
        private long CountEntries(DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel? level, string? machineName)
        {
            using (SqlConnection connection = new SqlConnection(fConnectionString))
            {
                using (SqlCommand command = connection.CreateCommand())
                {
                    ConfigureReadCommand(command, dateTimeUtcFrom, dateTimeUtcTo, level, machineName, true);
                    command.CommandText += ";";
                    connection.Open();
                    return (long)command.ExecuteScalar()!;
                }
            }
        }

        /// <summary>Applies identical validated filters to row retrieval and count queries.</summary>
        /// <param name="command">The command owned and disposed by the caller.</param>
        /// <param name="dateTimeUtcFrom">The inclusive UTC start of the interval.</param>
        /// <param name="dateTimeUtcTo">The exclusive UTC end of the interval.</param>
        /// <param name="level">The exact level, or null to include all levels.</param>
        /// <param name="machineName">The validated machine name, or null to include all machines.</param>
        /// <param name="countOnly">Whether to select a count instead of event columns.</param>
        /// <exception cref="ArgumentException">A timestamp is not UTC, the interval is reversed, or the level is undefined.</exception>
        private void ConfigureReadCommand(SqlCommand command, DateTime dateTimeUtcFrom, DateTime dateTimeUtcTo, EBlueprintLogLevel? level, string? machineName, bool countOnly)
        {
            if (dateTimeUtcFrom.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The interval start must use DateTimeKind.Utc.", nameof(dateTimeUtcFrom));
            }

            if (dateTimeUtcTo.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The interval end must use DateTimeKind.Utc.", nameof(dateTimeUtcTo));
            }

            if (dateTimeUtcFrom > dateTimeUtcTo)
            {
                throw new ArgumentException("The interval end cannot precede its start.", nameof(dateTimeUtcTo));
            }

            if (level.HasValue && !Enum.IsDefined(level.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "The log level is not defined.");
            }

            string columns = countOnly ? "COUNT_BIG(*)" : "[Id], [Logged], [Level], [Message], [Logger], [Exception], [MachineName]";
            command.CommandText = $"SELECT {columns} FROM {fTableName} WHERE [Logged] >= @from AND [Logged] < @to";

            // Preserve fractional query boundaries even though new tables store whole-second timestamps.
            SqlParameter from = command.Parameters.Add("@from", SqlDbType.DateTime2);
            from.Scale = 7;
            from.Value = dateTimeUtcFrom;
            SqlParameter to = command.Parameters.Add("@to", SqlDbType.DateTime2);
            to.Scale = 7;
            to.Value = dateTimeUtcTo;

            if (level.HasValue)
            {
                command.CommandText += " AND [Level] = @level";
                command.Parameters.Add("@level", SqlDbType.VarChar, 10).Value = level.Value.ToString();
            }

            if (machineName != null)
            {
                command.CommandText += " AND [MachineName] = @machineName";
                command.Parameters.Add("@machineName", SqlDbType.NVarChar, 255).Value = machineName;
            }
        }

        /// <summary>Validates a name for use as a single SQL identifier.</summary>
        /// <param name="identifier">The database or table name.</param>
        /// <param name="parameterName">The parameter name used in error reporting.</param>
        /// <exception cref="ArgumentException">The name is empty, too long, or contains control characters or surrounding whitespace.</exception>
        private static void ValidateIdentifier(string identifier, string parameterName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier, parameterName);

            if (identifier.Length > 128 || identifier != identifier.Trim() || identifier.Any(char.IsControl))
            {
                throw new ArgumentException("SQL names must contain 1 to 128 characters without control characters or surrounding whitespace.", parameterName);
            }
        }

        /// <summary>Creates a missing database while allowing another logger to initialize it concurrently.</summary>
        /// <param name="databaseName">The validated database name.</param>
        /// <exception cref="SqlException">Connecting to or creating the database fails.</exception>
        private void InitializeDatabase(string databaseName)
        {
            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(fConnectionString)
            {
                InitialCatalog = "master"
            };

            using (SqlConnection connection = new SqlConnection(settings.ConnectionString))
            {
                connection.Open();
                using (SqlCommand command = connection.CreateCommand())
                {
                    string quotedDatabaseName = "[" + databaseName.Replace("]", "]]") + "]";
                    command.CommandText = $"IF DB_ID(@databaseName) IS NULL CREATE DATABASE {quotedDatabaseName};";
                    command.Parameters.Add("@databaseName", SqlDbType.NVarChar, 128).Value = databaseName;

                    try
                    {
                        command.ExecuteNonQuery();
                    }
                    catch (SqlException exception) when (exception.Number == 1801)
                    {
                        // Another logger created the database after the existence check.
                    }
                }
            }
        }

        /// <summary>Creates the table without changing existing data and serializes concurrent initialization.</summary>
        /// <exception cref="SqlException">Connecting, acquiring the lock, or creating the table fails.</exception>
        private void InitializeTable()
        {
            using (SqlConnection connection = new SqlConnection(fConnectionString))
            {
                connection.Open();
                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    using (SqlCommand command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        // The shared "Blueprint.Log.Schema" application lock serializes the table existence check and creation across logger instances in this database.
                        // Exclusive mode lets one initializer proceed while others wait up to 30 seconds to acquire the lock before checking whether the table exists.
                        // Transaction ownership releases the lock automatically on commit or rollback.
                        // Nonnegative return codes indicate success; negative codes abort initialization through the THROW below.
                        // This lock is acquired only during initialization, not during normal log writes.
                        command.CommandText = $"""
                            DECLARE @lockResult int;
                            EXEC @lockResult = sys.sp_getapplock @Resource = N'Blueprint.Log.Schema', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
                            IF @lockResult < 0 THROW 51000, 'Could not acquire the log schema lock.', 1;
                            IF OBJECT_ID(@tableName, N'U') IS NULL
                            BEGIN
                                CREATE TABLE {fTableName}
                                (
                                    [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                    [Logged] datetime2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
                                    [Level] varchar(10) NOT NULL,
                                    [Message] nvarchar(max) NOT NULL,
                                    [Logger] nvarchar(500) NULL,
                                    [Exception] nvarchar(max) NULL,
                                    [MachineName] nvarchar(255) NOT NULL
                                );
                            END;
                            """;
                        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 266).Value = fTableName;
                        command.ExecuteNonQuery();
                        transaction.Commit();
                    }
                }
            }
        }
    }
}
