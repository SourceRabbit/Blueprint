using Blueprint.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace Blueprint.Database.Sql.RAMCache
{
    /// <summary>Synchronizes private snapshots of selected tables using SQL Change Tracking under the database lock.</summary>
    internal sealed class SqlEntityCache : IDisposable
    {
        private readonly SqlDatabase fDatabase;
        private readonly BlueprintDatabaseContext fContext;
        private readonly Dictionary<Type, RAMCachedTable> fRAMCachedTables = new Dictionary<Type, RAMCachedTable>();
        private readonly Timer fTimer;
        private TimeSpan fRefreshInterval;
        private long fLastRefresh;
        private bool fNeedsRefresh;
        private bool fIsDisposed;
        private int fRefreshIsRunning;

        /// <summary>Fully loads RAM entity tables, then starts periodic synchronization without caching related DB entities.</summary>
        /// <param name="database">The database that serializes context access.</param>
        /// <param name="context">The context after successful schema synchronization.</param>
        /// <param name="refreshInterval">The interval for checking external changes.</param>
        /// <exception cref="DbException">The initial data load fails.</exception>
        /// <exception cref="InvalidOperationException">An application transaction is active or Change Tracking is not enabled.</exception>
        internal SqlEntityCache(SqlDatabase database, BlueprintDatabaseContext context, TimeSpan refreshInterval)
        {
            fDatabase = database;
            fContext = context;
            fRefreshInterval = refreshInterval;
            foreach (Type contract in context.EntityTypes.GetRAMCachedContracts())
            {
                fRAMCachedTables.Add(contract, new RAMCachedTable(context, contract));
            }
            Refresh();
            fTimer = new Timer(RefreshInBackground, null, refreshInterval, refreshInterval);
        }

        /// <summary>Finds a cached table while preserving SQL reads inside application transactions.</summary>
        /// <param name="contract">The registered entity class.</param>
        /// <returns>The table, or null when no cached read is allowed.</returns>
        internal RAMCachedTable? GetTable(Type contract)
        {
            if (fIsDisposed || fContext.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null || !fRAMCachedTables.TryGetValue(contract, out RAMCachedTable? table))
            {
                return null;
            }
            return table;
        }

        /// <summary>Refreshes invalidated or expired snapshots before a supported cached read under the database lock.</summary>
        /// <exception cref="DbException">Required synchronization fails.</exception>
        /// <exception cref="InvalidOperationException">Change Tracking is unavailable or a transaction is active.</exception>
        internal void Synchronize()
        {
            if (fNeedsRefresh || Stopwatch.GetElapsedTime(fLastRefresh) >= fRefreshInterval)
            {
                Refresh();
            }
        }

        /// <summary>Requests synchronization before the next read following a write.</summary>
        internal void Invalidate()
        {
            fNeedsRefresh = true;
        }

        /// <summary>Changes the timer interval without losing already loaded data.</summary>
        /// <param name="refreshInterval">The validated new interval.</param>
        internal void setRefreshInterval(TimeSpan refreshInterval)
        {
            fRefreshInterval = refreshInterval;
            fTimer.Change(refreshInterval, refreshInterval);
        }

        /// <summary>Reads the version, valid history, and data in one snapshot transaction and publishes after commit.</summary>
        /// <exception cref="InvalidOperationException">An application transaction is active or Change Tracking is not enabled.</exception>
        /// <exception cref="DbException">Synchronization fails without publishing partial results.</exception>
        internal void Refresh()
        {
            if (fContext.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
            {
                throw new InvalidOperationException("RAM cache synchronization cannot run inside an application transaction.");
            }
            if (fIsDisposed || fRAMCachedTables.Count == 0)
            {
                return;
            }
            fNeedsRefresh = true;
            using IDbContextTransaction transaction = fContext.Database.BeginTransaction(IsolationLevel.Snapshot);
            using DbCommand command = SqlSchemaInitializer.CreateCommand(fContext, "SELECT CHANGE_TRACKING_CURRENT_VERSION();");
            object? versionValue = command.ExecuteScalar();
            if (versionValue is not long version)
            {
                throw new InvalidOperationException("SQL Server Change Tracking must remain enabled while RAMCached entities are in use.");
            }
            List<(RAMCachedTable Table, Dictionary<long, BlueprintEntity> Rows, long BeginVersion, int ObjectID)> snapshots = new List<(RAMCachedTable, Dictionary<long, BlueprintEntity>, long, int)>();
            foreach (RAMCachedTable table in fRAMCachedTables.Values)
            {
                (Dictionary<long, BlueprintEntity> rows, long beginVersion, int objectID) = table.ReadSnapshot(version);
                snapshots.Add((table, rows, beginVersion, objectID));
            }
            transaction.Commit();
            foreach ((RAMCachedTable table, Dictionary<long, BlueprintEntity> rows, long beginVersion, int objectID) in snapshots)
            {
                table.Publish(rows, version, beginVersion, objectID);
            }
            fLastRefresh = Stopwatch.GetTimestamp();
            fNeedsRefresh = false;
        }

        /// <summary>Checks external changes without concurrent context use and requires the reader to retry after failure.</summary>
        /// <param name="state">The unused timer state.</param>
        private void RefreshInBackground(object? state)
        {
            if (Interlocked.Exchange(ref fRefreshIsRunning, 1) != 0)
            {
                return;
            }
            try
            {
                fDatabase.ExecuteOperation(context =>
                {
                    if (!fIsDisposed && ReferenceEquals(context, fContext))
                    {
                        try
                        {
                            Refresh();
                        }
                        catch (Exception)
                        {
                            // The next read retries the check and receives the exception instead of silently returning stale data.
                            fNeedsRefresh = true;
                        }
                    }
                    return 0;
                });
            }
            catch (ObjectDisposedException)
            {
                // A callback that waited for the lock may arrive after Shutdown.
            }
            finally
            {
                Volatile.Write(ref fRefreshIsRunning, 0);
            }
        }

        /// <summary>Stops new checks and releases snapshots under the database lock.</summary>
        public void Dispose()
        {
            fIsDisposed = true;
            fTimer.Dispose();
            fRAMCachedTables.Clear();
        }
    }
}
