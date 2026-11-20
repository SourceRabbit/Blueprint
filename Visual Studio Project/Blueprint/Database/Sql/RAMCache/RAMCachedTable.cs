// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Reflection;

namespace Blueprint.Database.Sql.RAMCache
{
    /// <summary>Stores a table's committed scalar values and corresponding synchronization position.</summary>
    internal sealed class RAMCachedTable
    {
        /// <summary>A pending table snapshot. Null ChangedKeys invalidates every index after a full reload or an insertion/deletion.</summary>
        internal sealed record Snapshot(Dictionary<long, BlueprintEntity> Rows, long BeginVersion, int ObjectID, HashSet<string>? ChangedKeys = null);

        private const int CHANGED_ID_BATCH_SIZE = 1000;
        private readonly object fSyncRoot = new();
        private readonly Type fConcreteType;
        private readonly IProperty[] fProperties;
        private readonly Dictionary<string, IProperty> fLookupKeys;
        private readonly IProperty[] fIndexKeys;
        private readonly Dictionary<string, ILookup<object?, long>> fForeignKeyIndexes = new(StringComparer.Ordinal);
        private readonly string fTableName;
        private readonly string fKeyColumn;
        private readonly Func<BlueprintDatabaseContext, string, object[], IEnumerable<BlueprintEntity>> fReadRows;
        private Dictionary<long, BlueprintEntity> fRows = new Dictionary<long, BlueprintEntity>();
        private Dictionary<long, int>? fRowPositions;
        private long? fVersion;
        private long fBeginVersion;
        private int fObjectID;

        /// <summary>Prepares scalar snapshots for an entity whose base class selects RAM storage.</summary>
        /// <param name="context">The active model context.</param>
        /// <param name="contract">The RAM entity class.</param>
        internal RAMCachedTable(BlueprintDatabaseContext context, Type contract)
        {
            fConcreteType = context.EntityTypes.Types[contract];
            IEntityType entityType = context.Model.FindEntityType(fConcreteType)!;
            fProperties = entityType.GetProperties().ToArray();
            fLookupKeys = fProperties.Where(property => property.PropertyInfo != null && (property.ClrType == typeof(long) || property.ClrType == typeof(long?)))
                .Where(property => property.IsPrimaryKey() || property.GetContainingForeignKeys().Any(key => key.Properties.Count == 1)).ToDictionary(property => property.Name, StringComparer.Ordinal);
            fIndexKeys = fProperties.Where(property => property.PropertyInfo != null && property.Name != nameof(BlueprintEntity.ID) && (property.IsKey() || property.IsForeignKey())).ToArray();
            ISqlGenerationHelper sqlHelper = context.GetService<ISqlGenerationHelper>();
            fTableName = sqlHelper.DelimitIdentifier(entityType.GetTableName()!, entityType.GetSchema());
            StoreObjectIdentifier store = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
            fKeyColumn = sqlHelper.DelimitIdentifier(entityType.FindPrimaryKey()!.Properties.Single().GetColumnName(store)!);
            fReadRows = typeof(RAMCachedTable).GetMethod(nameof(ReadRows), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(fConcreteType).CreateDelegate<Func<BlueprintDatabaseContext, string, object[], IEnumerable<BlueprintEntity>>>();
        }

        /// <summary>Prepares a new snapshot using a delta or a full reload when the previous version is no longer valid.</summary>
        /// <param name="context">The context reserved for the snapshot transaction.</param>
        /// <param name="version">The version of the current snapshot transaction.</param>
        /// <returns>The new data and the identity of the active history.</returns>
        /// <exception cref="InvalidOperationException">Change Tracking is not enabled on the selected table.</exception>
        internal Snapshot ReadSnapshot(BlueprintDatabaseContext context, long version)
        {
            long minimumVersion;
            long beginVersion;
            int objectID;
            using (DbCommand command = SqlSchemaInitializer.CreateCommand(context, "SELECT CHANGE_TRACKING_MIN_VALID_VERSION(object_id), begin_version, object_id FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(@table);"))
            {
                command.Parameters.Add(new SqlParameter("@table", fTableName));
                using (DbDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1))
                    {
                        throw new InvalidOperationException($"Change Tracking is unavailable for RAMCached table '{fTableName}'. Enable it and grant SELECT and VIEW CHANGE TRACKING permissions.");
                    }
                    minimumVersion = reader.GetInt64(0);
                    beginVersion = reader.GetInt64(1);
                    objectID = reader.GetInt32(2);
                }
            }
            // TRUNCATE or re-enabling tracking may retain the same version if no tracked DML occurred in between.
            if (!fVersion.HasValue || fVersion.Value <= minimumVersion || fVersion.Value > version || fBeginVersion != beginVersion || fObjectID != objectID)
            {
                return new Snapshot(fReadRows(context, $"SELECT * FROM {fTableName}", Array.Empty<object>()).ToDictionary(entity => entity.ID), beginVersion, objectID);
            }
            if (fVersion.Value == version)
            {
                return new Snapshot(fRows, beginVersion, objectID);
            }
            List<long> changedIDs = new List<long>();
            using (DbCommand command = SqlSchemaInitializer.CreateCommand(context, $"SELECT {fKeyColumn} FROM CHANGETABLE(CHANGES {fTableName}, @cacheVersion) AS changes;"))
            {
                command.Parameters.Add(new SqlParameter("@cacheVersion", fVersion.Value));
                using (DbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        changedIDs.Add(reader.GetInt64(0));
                    }
                }
            }
            if (changedIDs.Count == 0)
            {
                return new Snapshot(fRows, beginVersion, objectID);
            }
            Dictionary<long, BlueprintEntity> rows = new Dictionary<long, BlueprintEntity>(fRows);
            HashSet<long> missingIDs = changedIDs.ToHashSet();
            HashSet<string>? changedKeys = new(StringComparer.Ordinal);
            foreach (long[] batch in changedIDs.Chunk(CHANGED_ID_BATCH_SIZE))
            {
                SqlParameter[] parameters = batch.Select((id, index) => new SqlParameter($"@id{index}", id)).ToArray();
                string keyParameters = string.Join(", ", parameters.Select(parameter => parameter.ParameterName));
                string sql = $"SELECT * FROM {fTableName} WHERE {fKeyColumn} IN ({keyParameters})";
                foreach (BlueprintEntity entity in fReadRows(context, sql, parameters))
                {
                    missingIDs.Remove(entity.ID);
                    if (!fRows.TryGetValue(entity.ID, out BlueprintEntity? previous))
                    {
                        changedKeys = null;
                    }
                    else if (changedKeys != null)
                    {
                        foreach (IProperty key in fIndexKeys)
                        {
                            PropertyInfo property = key.PropertyInfo!;
                            if (!Equals(property.GetValue(previous), property.GetValue(entity)))
                            {
                                changedKeys.Add(key.Name);
                            }
                        }
                    }
                    rows[entity.ID] = entity;
                }
            }
            foreach (long id in missingIDs)
            {
                rows.Remove(id);
            }
            return new Snapshot(rows, beginVersion, objectID, missingIDs.Count == 0 ? changedKeys : null);
        }

        /// <summary>Publishes only data read and validated in the same successful transaction.</summary>
        /// <param name="snapshot">The committed rows, table identity and changes to indexed keys.</param>
        /// <param name="version">The new synchronization position.</param>
        internal void Publish(Snapshot snapshot, long version)
        {
            lock (fSyncRoot)
            {
                if (snapshot.ChangedKeys != null)
                {
                    foreach (string key in snapshot.ChangedKeys)
                    {
                        fForeignKeyIndexes.Remove(key);
                    }
                }
                else if (!ReferenceEquals(fRows, snapshot.Rows))
                {
                    fForeignKeyIndexes.Clear();
                    fRowPositions = null;
                }
                fRows = snapshot.Rows;
                fVersion = version;
                fBeginVersion = snapshot.BeginVersion;
                fObjectID = snapshot.ObjectID;
            }
        }

        /// <summary>Copies only mapped scalar values without exposing the shared cached instance.</summary>
        /// <param name="stored">The shared cached row.</param>
        /// <param name="language">The language of the independent copy.</param>
        /// <returns>An independent entity; relationship executors load its AutoIncludes separately.</returns>
        internal BlueprintEntity Copy(BlueprintEntity stored, long language)
        {
            BlueprintEntity entity = (BlueprintEntity)Activator.CreateInstance(fConcreteType)!;
            foreach (IProperty property in fProperties)
            {
                object? value = property.PropertyInfo!.GetValue(stored);
                object? copy = value is Array array ? array.Clone() : (property.GetValueComparer() ?? property.GetTypeMapping().Comparer).Snapshot(value);
                property.PropertyInfo.SetValue(entity, copy);
            }
            entity.CurrentLanguage = language;
            return entity;
        }

        /// <summary>Looks up related cached rows without scanning the full table for every parent.</summary>
        /// <param name="key">The primary or foreign key to match.</param>
        /// <param name="ids">The distinct parent key values.</param>
        /// <returns>Shared scalar rows that the executor must copy before returning.</returns>
        internal IEnumerable<BlueprintEntity> GetRelatedRows(IProperty key, long[] ids)
        {
            lock (fSyncRoot)
            {
                List<BlueprintEntity> rows = new List<BlueprintEntity>();
                foreach (long id in ids)
                {
                    if (key.Name == nameof(BlueprintEntity.ID))
                    {
                        if (fRows.TryGetValue(id, out BlueprintEntity? entity))
                        {
                            rows.Add(entity);
                        }
                    }
                    else
                    {
                        foreach (long rowID in GetOrCreateForeignKeyIndex(key)[id])
                        {
                            rows.Add(fRows[rowID]);
                        }
                    }
                }
                return rows;
            }
        }

        /// <summary>Reads distinct key hits from one snapshot and restores its row order for stable paging and sorting.</summary>
        /// <param name="key">The indexed primary or foreign key.</param>
        /// <param name="ids">The requested values; duplicate or missing keys do not add rows.</param>
        /// <returns>The matching rows in their original snapshot order.</returns>
        internal IEnumerable<BlueprintEntity> GetRowsByKeys(IProperty key, IEnumerable<long?> ids)
        {
            if (ids.TryGetNonEnumeratedCount(out int count) && count == 0)
            {
                return Array.Empty<BlueprintEntity>();
            }
            Dictionary<long, BlueprintEntity> snapshot;
            ILookup<object?, long>? groups;
            Dictionary<long, int> positions;
            lock (fSyncRoot)
            {
                snapshot = fRows;
                groups = key.IsPrimaryKey() ? null : GetOrCreateForeignKeyIndex(key);
                if (fRowPositions == null)
                {
                    Dictionary<long, int> order = new(snapshot.Count);
                    foreach (long id in snapshot.Keys)
                    {
                        order.Add(id, order.Count);
                    }
                    fRowPositions = order;
                }
                positions = fRowPositions;
            }
            List<BlueprintEntity> matches = new();
            foreach (long? id in ids.Distinct())
            {
                if (groups != null)
                {
                    foreach (long rowID in groups[id])
                    {
                        matches.Add(snapshot[rowID]);
                    }
                }
                else if (id.HasValue && snapshot.TryGetValue(id.Value, out BlueprintEntity? entity))
                {
                    matches.Add(entity);
                }
            }
            return matches.Count < 2 ? matches : matches.OrderBy(entity => positions[entity.ID]).ToArray();
        }

        /// <summary>Resolves one indexed group against the rows published with that index.</summary>
        /// <param name="foreignKey">The mapped dependent key.</param>
        /// <param name="id">The requested parent ID, including null for unassigned rows.</param>
        /// <returns>The matching rows in snapshot order, or an empty group when no rows match.</returns>
        internal IEnumerable<BlueprintEntity> GetForeignKeyRows(IProperty foreignKey, long? id)
        {
            lock (fSyncRoot)
            {
                Dictionary<long, BlueprintEntity> snapshot = fRows;
                return GetOrCreateForeignKeyIndex(foreignKey)[id].Select(rowID => snapshot[rowID]);
            }
        }

        /// <summary>Groups row IDs by key independently of the scalar row instances. The caller holds the table lock.</summary>
        /// <param name="foreignKey">The mapped foreign key to index.</param>
        /// <returns>Groups of row IDs in snapshot order, keyed by their stored foreign-key value.</returns>
        private ILookup<object?, long> GetOrCreateForeignKeyIndex(IProperty foreignKey)
        {
            if (!fForeignKeyIndexes.TryGetValue(foreignKey.Name, out ILookup<object?, long>? index))
            {
                index = fRows.Values.ToLookup(row => foreignKey.PropertyInfo!.GetValue(row), row => row.ID);
                fForeignKeyIndexes.Add(foreignKey.Name, index);
            }
            return index;
        }

        /// <summary>Uses the EF Core materializer to read CLR types and physical columns correctly.</summary>
        /// <typeparam name="TEntity">The generated CLR type.</typeparam>
        /// <param name="context">The snapshot transaction context.</param>
        /// <param name="sql">The SQL with safely delimited identifiers.</param>
        /// <param name="parameters">The Change Tracking parameters.</param>
        /// <returns>Untracked rows to enumerate within the snapshot transaction.</returns>
        private static IEnumerable<BlueprintEntity> ReadRows<TEntity>(BlueprintDatabaseContext context, string sql, object[] parameters) where TEntity : BlueprintEntity
        {
            return context.Set<TEntity>().FromSqlRaw(sql, parameters).AsNoTracking();
        }

        /// <summary>Finds a mapped long primary key or single-column foreign key by its entity property name.</summary>
        /// <param name="name">The property referenced by the filter on the public entity contract.</param>
        /// <returns>The mapped key, or null if that property has no supported lookup.</returns>
        internal IProperty? getLookupKey(string name)
        {
            return fLookupKeys.GetValueOrDefault(name);
        }

        /// <summary>The generated CLR type of the stored entities.</summary>
        internal Type ConcreteType
        {
            get
            {
                return fConcreteType;
            }
        }

        /// <summary>The scalar model properties available in the snapshot.</summary>
        internal IProperty[] Properties
        {
            get
            {
                return fProperties;
            }
        }

        /// <summary>The current immutable row dictionary, safe to enumerate after a newer snapshot is published.</summary>
        internal IReadOnlyDictionary<long, BlueprintEntity> Rows
        {
            get
            {
                lock (fSyncRoot)
                {
                    return fRows;
                }
            }
        }
    }
}
