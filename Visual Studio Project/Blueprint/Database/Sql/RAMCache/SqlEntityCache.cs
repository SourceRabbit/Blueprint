// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace Blueprint.Database.Sql.RAMCache
{
    /// <summary>Synchronizes private snapshots of selected tables using SQL Change Tracking under the cache lock.</summary>
    internal sealed class SqlEntityCache : IDisposable
    {
        private readonly object fSyncRoot = new();
        private readonly BlueprintEntityTypes fEntityTypes;
        private readonly Dictionary<Type, RAMCachedTable> fRAMCachedTables = new Dictionary<Type, RAMCachedTable>();
        private readonly Timer fTimer;
        private TimeSpan fRefreshInterval;
        private long fLastRefresh;
        private bool fNeedsRefresh;
        private bool fIsDisposed;
        private int fRefreshIsRunning;

        /// <summary>Fully loads RAM entity tables and prepares a stopped timer until the context and cache are published.</summary>
        /// <param name="context">The context after successful schema synchronization.</param>
        /// <param name="refreshInterval">The interval for checking external changes.</param>
        /// <exception cref="DbException">The initial data load fails.</exception>
        /// <exception cref="InvalidOperationException">An application transaction is active or Change Tracking is not enabled.</exception>
        internal SqlEntityCache(BlueprintDatabaseContext context, TimeSpan refreshInterval)
        {
            fEntityTypes = context.EntityTypes;
            fRefreshInterval = refreshInterval;
            foreach (Type contract in context.EntityTypes.GetRAMCachedContracts())
            {
                fRAMCachedTables.Add(contract, new RAMCachedTable(context, contract));
            }
            Refresh(context);
            fTimer = new Timer(RefreshInBackground, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>Starts periodic refresh after the database has activated and published this cache under its lock.</summary>
        internal void Start()
        {
            lock (fSyncRoot)
            {
                fTimer.Change(fRefreshInterval, fRefreshInterval);
            }
        }

        /// <summary>Finds a cached table while preserving SQL reads inside application transactions.</summary>
        /// <param name="contract">The registered entity class.</param>
        /// <param name="context">The context reserved for the current operation.</param>
        /// <returns>The table, or null when no cached read is allowed.</returns>
        internal RAMCachedTable? GetTable(Type contract, BlueprintDatabaseContext context)
        {
            lock (fSyncRoot)
            {
                if (fIsDisposed || context.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null || !fRAMCachedTables.TryGetValue(contract, out RAMCachedTable? table))
                {
                    return null;
                }
                return table;
            }
        }

        /// <summary>Refreshes invalidated or expired snapshots before a supported cached read under the cache lock.</summary>
        /// <param name="context">The context reserved for the current operation.</param>
        /// <exception cref="DbException">Required synchronization fails.</exception>
        /// <exception cref="InvalidOperationException">Change Tracking is unavailable or a transaction is active.</exception>
        internal void Synchronize(BlueprintDatabaseContext context)
        {
            lock (fSyncRoot)
            {
                if (fNeedsRefresh || Stopwatch.GetElapsedTime(fLastRefresh) >= fRefreshInterval)
                {
                    Refresh(context);
                }
            }
        }

        /// <summary>Requests synchronization before the next read following a write.</summary>
        internal void Invalidate()
        {
            lock (fSyncRoot)
            {
                fNeedsRefresh = true;
            }
        }

        /// <summary>Changes the timer interval without losing already loaded data.</summary>
        /// <param name="refreshInterval">The validated new interval.</param>
        internal void setRefreshInterval(TimeSpan refreshInterval)
        {
            lock (fSyncRoot)
            {
                fRefreshInterval = refreshInterval;
                fTimer.Change(refreshInterval, refreshInterval);
            }
        }

        /// <summary>Reads the version, valid history, and data in one snapshot transaction and publishes after commit.</summary>
        /// <param name="context">The context reserved for the snapshot transaction.</param>
        /// <exception cref="InvalidOperationException">An application transaction is active or Change Tracking is not enabled.</exception>
        /// <exception cref="DbException">Synchronization fails without publishing partial results.</exception>
        internal void Refresh(BlueprintDatabaseContext context)
        {
            lock (fSyncRoot)
            {
                if (context.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
                {
                    throw new InvalidOperationException("RAM cache synchronization cannot run inside an application transaction.");
                }
                if (fIsDisposed || fRAMCachedTables.Count == 0)
                {
                    return;
                }
                fNeedsRefresh = true;
                using (IDbContextTransaction transaction = context.Database.BeginTransaction(IsolationLevel.Snapshot))
                using (DbCommand command = SqlSchemaInitializer.CreateCommand(context, "SELECT CHANGE_TRACKING_CURRENT_VERSION();"))
                {
                    object? versionValue = command.ExecuteScalar();
                    if (versionValue is not long version)
                    {
                        throw new InvalidOperationException("SQL Server Change Tracking must remain enabled while RAMCached entities are in use.");
                    }
                    List<(RAMCachedTable Table, RAMCachedTable.Snapshot Snapshot)> snapshots = new();
                    foreach (RAMCachedTable table in fRAMCachedTables.Values)
                    {
                        snapshots.Add((table, table.ReadSnapshot(context, version)));
                    }
                    transaction.Commit();
                    foreach ((RAMCachedTable table, RAMCachedTable.Snapshot snapshot) in snapshots)
                    {
                        table.Publish(snapshot, version);
                    }
                }
                fLastRefresh = Stopwatch.GetTimestamp();
                fNeedsRefresh = false;
            }
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
                BlueprintDatabase? database = BlueprintDatabase.ACTIVE_INSTANCE;
                if (database == null)
                {
                    // Disposal may clear the active instance before an already queued callback starts.
                    return;
                }
                // Coordinate the timer with initialization and disposal without serializing consumer operations.
                lock (database.SyncRoot)
                {
                    database.ExecuteOperation(context =>
                    {
                        if (ReferenceEquals(context.EntityTypes, fEntityTypes))
                        {
                            try
                            {
                                Refresh(context);
                            }
                            catch (Exception)
                            {
                                // The next read retries the check and receives the exception instead of silently returning stale data.
                                Invalidate();
                            }
                        }
                        return 0;
                    });
                }
            }
            catch (ObjectDisposedException)
            {
                // A callback waiting for a pooled context may arrive after disposal.
            }
            finally
            {
                Volatile.Write(ref fRefreshIsRunning, 0);
            }
        }

        /// <summary>Stops new checks and releases snapshots under the cache lock.</summary>
        public void Dispose()
        {
            lock (fSyncRoot)
            {
                fIsDisposed = true;
                fTimer.Dispose();
                fRAMCachedTables.Clear();
            }
        }
    }
}
