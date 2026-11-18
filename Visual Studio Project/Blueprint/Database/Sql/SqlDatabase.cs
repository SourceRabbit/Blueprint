using System;
using System.Linq;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Blueprint.Entities;
using Blueprint.Database.Sql.RAMCache;
using Blueprint.Fundamentals;

namespace Blueprint.Database.Sql
{
    /// <summary>
    /// Implements SQL Server access through EF Core.
    /// </summary>
    public sealed class SqlDatabase : BlueprintDatabase
    {
        /// <summary>
        /// Creates the database if missing and activates its initialized context for the Entity API before returning.
        /// On failure, releases resources before propagating the exception to the caller.
        /// The name overrides Database or Initial Catalog in the connection string.
        /// </summary>
        /// <param name="name">The application database name.</param>
        /// <param name="connectionString">The SQL Server connection settings.</param>
        /// <param name="languages">The model languages, with unique IDs and prefixes and exactly one language with an empty Prefix.</param>
        /// <exception cref="ArgumentNullException">No language list was provided.</exception>
        /// <exception cref="ArgumentException">The name, connection settings, or languages are invalid.</exception>
        /// <exception cref="InvalidOperationException">Another database is active.</exception>
        /// <exception cref="SqlException">Connecting to or creating the database fails.</exception>
        /// <exception cref="Exception">The initialization failure is propagated after resources are released.</exception>
        public SqlDatabase(string name, string connectionString, List<BlueprintLanguage> languages) : base(name, NormalizeConnectionString(name, connectionString), languages)
        {
            try
            {
                CreateDatabase();
                SynchronizeEntities();
            }
            catch
            {
                Dispose();
                throw;
            }
        }


        /// <summary>Executes SQL Server command text on an independent connection and invalidates the local RAM cache.</summary>
        /// <param name="commandText">The nonempty SQL Server command text.</param>
        /// <param name="parameters">SqlParameter instances, or null when no parameters are required. Null values are sent as DBNull.Value.</param>
        /// <param name="commandTimeoutSeconds">The nonnegative timeout in seconds; null uses the connection's command default and zero disables the timeout.</param>
        /// <returns>The affected-row count reported by SQL Server, or -1 when a count is unavailable.</returns>
        /// <remarks>
        /// The command runs without the shared entity context or database lock and never enlists in application transactions.
        /// Parameters are detached before returning, including on failure; do not modify or share them across concurrent commands.
        /// Dispose prevents new calls but does not cancel an independent command that has already passed the lifetime check.
        /// </remarks>
        /// <exception cref="ArgumentException">The command is empty, a parameter is null or incompatible, or a parameter already belongs to a command.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is negative.</exception>
        /// <exception cref="InvalidOperationException">The caller is inside a database operation or an ambient transaction.</exception>
        /// <exception cref="ObjectDisposedException">The database has been disposed before the call is accepted.</exception>
        /// <exception cref="SqlException">Opening the connection or executing the SQL command fails.</exception>
        public override int ExecuteNonQuery(string commandText, IReadOnlyList<DbParameter>? parameters = null, int? commandTimeoutSeconds = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

            if (commandTimeoutSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds), "The command timeout must be nonnegative.");
            }

            if (IsExecutingOperation || Monitor.IsEntered(fSyncRoot) || System.Transactions.Transaction.Current != null)
            {
                throw new InvalidOperationException("Execute standalone SQL outside an existing database operation or TransactionScope.");
            }

            lock (fSyncRoot)
            {
                //////////////////////////////////////////////////////////////////////////////////////////////////////////
                // Synchronize call acceptance with Dispose so a disposed instance cannot accept new commands.
                // The lock is released before opening the independent connection or executing SQL.
                //////////////////////////////////////////////////////////////////////////////////////////////////////////
                ObjectDisposedException.ThrowIf(fIsDisposed, this);
                //////////////////////////////////////////////////////////////////////////////////////////////////////////
            }

            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(ConnectionString) { Enlist = false };
            bool executionAttempted = false;
            try
            {
                using (SqlConnection connection = new SqlConnection(settings.ConnectionString))
                {
                    using (SqlCommand command = connection.CreateCommand())
                    {
                        command.CommandText = commandText;

                        if (commandTimeoutSeconds.HasValue)
                        {
                            command.CommandTimeout = commandTimeoutSeconds.Value;
                        }

                        try
                        {
                            if (parameters != null)
                            {
                                foreach (DbParameter parameter in parameters)
                                {
                                    if (parameter is not SqlParameter sqlParameter)
                                    {
                                        throw new ArgumentException("Every parameter must be a non-null Microsoft.Data.SqlClient.SqlParameter.", nameof(parameters));
                                    }
                                    command.Parameters.Add(sqlParameter);
                                    sqlParameter.Value ??= DBNull.Value;
                                }
                            }
                            executionAttempted = true;
                            connection.Open();
                            return command.ExecuteNonQuery();
                        }
                        finally
                        {
                            command.Parameters.Clear();
                        }
                    }
                }
            }
            finally
            {
                if (executionAttempted)
                {
                    lock (fSyncRoot)
                    {
                        if (!fIsDisposed)
                        {
                            InvalidateCache();
                        }
                    }
                }
            }
        }

        /// <summary>Creates the configured SQL Server database only if it does not already exist.</summary>
        /// <exception cref="SqlException">Connecting to or creating the database fails.</exception>
        private void CreateDatabase()
        {
            SqlConnectionStringBuilder connectionStringBuilder = new SqlConnectionStringBuilder(ConnectionString)
            {
                InitialCatalog = "master"
            };

            using (SqlConnection connection = new SqlConnection(connectionStringBuilder.ConnectionString))
            {
                connection.Open();

                using (SqlCommand command = connection.CreateCommand())
                {
                    string quotedDatabaseName = "[" + Name.Replace("]", "]]") + "]";
                    command.CommandText = $"IF DB_ID(@databaseName) IS NULL CREATE DATABASE {quotedDatabaseName};";
                    command.Parameters.AddWithValue("@databaseName", Name);
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Synchronizes the schema and fully loads RAM-cached entities after each registration, retaining the previous model on failure.</summary>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        /// <exception cref="InvalidOperationException">An entity has an invalid table mapping or the cache cannot be synchronized.</exception>
        /// <exception cref="NotSupportedException">The mapping requires a table rename.</exception>
        /// <exception cref="SqlException">Connection, database or table creation, or initial cache loading fails.</exception>
        protected override void SynchronizeEntities()
        {
            lock (fSyncRoot)
            {
                ObjectDisposedException.ThrowIf(fIsDisposed, this);

                SqlEntityCache? cache = null;
                try
                {
                    base.InitializeContext(new DbContextOptionsBuilder().UseSqlServer(ConnectionString), context =>
                    {
                        SqlChangeTracking.InitializeDatabase(context);
                        SqlSchemaInitializer.Initialize(context);
                        BlueprintDatabaseContext entityContext = (BlueprintDatabaseContext)context;
                        cache = entityContext.EntityTypes.Types.Keys.Any(contract => typeof(BlueprintEntityRAM).IsAssignableFrom(contract)) ? new SqlEntityCache(entityContext, RAMCacheRefreshInterval) : null;
                    });
                }
                catch
                {
                    cache?.Dispose();
                    throw;
                }
                fRAMCache?.Dispose();
                fRAMCache = cache;
                fRAMCache?.Start();
            }
        }

        /// <summary>Finds a cached entity table when the current transaction permits cached reads.</summary>
        /// <param name="contract">The registered entity class.</param>
        /// <param name="context">The context reserved for the current operation.</param>
        /// <returns>The cached table, or null when SQL is required.</returns>
        internal override RAMCachedTable? GetCachedTable(Type contract, BlueprintDatabaseContext context)
        {
            return fRAMCache?.GetTable(contract, context);
        }

        /// <summary>Refreshes invalidated or expired snapshots before executing a supported RAM query.</summary>
        /// <param name="context">The context reserved for the current query.</param>
        /// <exception cref="Exception">Required cache synchronization fails.</exception>
        internal override void SynchronizeCache(BlueprintDatabaseContext context)
        {
            fRAMCache?.Synchronize(context);
        }

        /// <summary>Requires cache synchronization after a write, including potential trigger changes.</summary>
        internal override void InvalidateCache()
        {
            fRAMCache?.Invalidate();
        }

        /// <summary>Stops the timer and releases cached data before closing the SQL context.</summary>
        protected override void DisposeResources()
        {
            fRAMCache?.Dispose();
            fRAMCache = null;
        }

        /// <summary>Validates settings and incorporates the database name into the connection string.</summary>
        /// <param name="name">The application database name.</param>
        /// <param name="connectionString">The original connection settings.</param>
        /// <returns>A connection string with Initial Catalog set to name.</returns>
        /// <exception cref="ArgumentException">The settings are invalid or refer to a system database.</exception>
        private static string NormalizeConnectionString(string name, string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            if (name.Length > 128 || name != name.Trim() || name.Any(char.IsControl))
            {
                throw new ArgumentException("The database name must contain 1 to 128 characters without control characters or surrounding whitespace.", nameof(name));
            }

            if (new[] { "master", "model", "msdb", "tempdb" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A system database cannot be used as the application database.", nameof(name));
            }

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = name
            };

            if (string.IsNullOrWhiteSpace(builder.DataSource) || !string.IsNullOrEmpty(builder.AttachDBFilename))
            {
                throw new ArgumentException("Specify Server without AttachDBFilename.", nameof(connectionString));
            }

            return builder.ConnectionString;
        }
    }
}
