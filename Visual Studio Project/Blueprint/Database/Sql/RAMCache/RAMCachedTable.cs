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
        private readonly object fSyncRoot = new();
        private readonly Type fConcreteType;
        private readonly IProperty[] fProperties;
        private readonly Dictionary<IProperty, ILookup<object?, BlueprintEntity>> fForeignKeyIndexes = new Dictionary<IProperty, ILookup<object?, BlueprintEntity>>();
        private readonly string fTableName;
        private readonly string fKeyColumn;
        private readonly Func<BlueprintDatabaseContext, string, object[], List<BlueprintEntity>> fReadRows;
        private Dictionary<long, BlueprintEntity> fRows = new Dictionary<long, BlueprintEntity>();
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
            ISqlGenerationHelper sqlHelper = context.GetService<ISqlGenerationHelper>();
            fTableName = sqlHelper.DelimitIdentifier(entityType.GetTableName()!, entityType.GetSchema());
            StoreObjectIdentifier store = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
            fKeyColumn = sqlHelper.DelimitIdentifier(entityType.FindPrimaryKey()!.Properties.Single().GetColumnName(store)!);
            fReadRows = typeof(RAMCachedTable).GetMethod(nameof(ReadRows), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(fConcreteType).CreateDelegate<Func<BlueprintDatabaseContext, string, object[], List<BlueprintEntity>>>();
        }

        /// <summary>Prepares a new snapshot using a delta or a full reload when the previous version is no longer valid.</summary>
        /// <param name="context">The context reserved for the snapshot transaction.</param>
        /// <param name="version">The version of the current snapshot transaction.</param>
        /// <returns>The new data and the identity of the active history.</returns>
        /// <exception cref="InvalidOperationException">Change Tracking is not enabled on the selected table.</exception>
        internal (Dictionary<long, BlueprintEntity> Rows, long BeginVersion, int ObjectID) ReadSnapshot(BlueprintDatabaseContext context, long version)
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
                return (fReadRows(context, $"SELECT * FROM {fTableName}", Array.Empty<object>()).ToDictionary(entity => entity.ID), beginVersion, objectID);
            }
            if (fVersion.Value == version)
            {
                return (fRows, beginVersion, objectID);
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
                return (fRows, beginVersion, objectID);
            }
            Dictionary<long, BlueprintEntity> rows = new Dictionary<long, BlueprintEntity>(fRows);
            foreach (long id in changedIDs)
            {
                rows.Remove(id);
            }
            string sql = $"SELECT source.* FROM {fTableName} AS source INNER JOIN CHANGETABLE(CHANGES {fTableName}, @cacheVersion) AS changes ON source.{fKeyColumn} = changes.{fKeyColumn}";
            foreach (BlueprintEntity entity in fReadRows(context, sql, new object[] { new SqlParameter("@cacheVersion", fVersion.Value) }))
            {
                rows[entity.ID] = entity;
            }
            return (rows, beginVersion, objectID);
        }

        /// <summary>Publishes only data read and validated in the same successful transaction.</summary>
        /// <param name="rows">The complete data of the new snapshot.</param>
        /// <param name="version">The new synchronization position.</param>
        /// <param name="beginVersion">The start of the active Change Tracking history.</param>
        /// <param name="objectID">The SQL table identity.</param>
        internal void Publish(Dictionary<long, BlueprintEntity> rows, long version, long beginVersion, int objectID)
        {
            lock (fSyncRoot)
            {
                if (!ReferenceEquals(fRows, rows))
                {
                    fForeignKeyIndexes.Clear();
                }
                fRows = rows;
                fVersion = version;
                fBeginVersion = beginVersion;
                fObjectID = objectID;
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
                        foreach (BlueprintEntity entity in GetReferencingRows(key, id))
                        {
                            rows.Add(entity);
                        }
                    }
                }
                return rows;
            }
        }

        /// <summary>Indexes dependent rows once per snapshot to avoid scanning the table for every included collection.</summary>
        /// <param name="foreignKey">The mapped foreign key on this table.</param>
        /// <param name="id">The principal row's ID.</param>
        /// <returns>The dependent rows belonging to the principal.</returns>
        private IEnumerable<BlueprintEntity> GetReferencingRows(IProperty foreignKey, long id)
        {
            if (!fForeignKeyIndexes.TryGetValue(foreignKey, out ILookup<object?, BlueprintEntity>? index))
            {
                index = fRows.Values.ToLookup(row => foreignKey.PropertyInfo!.GetValue(row));
                fForeignKeyIndexes.Add(foreignKey, index);
            }
            return index[id];
        }

        /// <summary>Uses the EF Core materializer to read CLR types and physical columns correctly.</summary>
        /// <typeparam name="TEntity">The generated CLR type.</typeparam>
        /// <param name="context">The snapshot transaction context.</param>
        /// <param name="sql">The SQL with safely delimited identifiers.</param>
        /// <param name="parameters">The Change Tracking parameters.</param>
        /// <returns>The stored rows without tracking or relationships.</returns>
        private static List<BlueprintEntity> ReadRows<TEntity>(BlueprintDatabaseContext context, string sql, object[] parameters) where TEntity : BlueprintEntity
        {
            return context.Set<TEntity>().FromSqlRaw(sql, parameters).AsNoTracking().ToList().Cast<BlueprintEntity>().ToList();
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
