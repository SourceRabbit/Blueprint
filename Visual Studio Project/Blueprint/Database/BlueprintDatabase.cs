using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Blueprint.Entities;
using Blueprint.Database.Queries;
using Blueprint.Database.Sql;
using Blueprint.Database.Sql.RAMCache;
using DatabaseTransactionFactory = Blueprint.Database.TransactionFactory.TransactionFactory;
using Blueprint.Fundamentals;
using Blueprint.Localization;
using System.Data.Common;

namespace Blueprint.Database
{
    /// <summary>Manages entity contracts, languages, and the active database used by the Entity API.</summary>
    public abstract class BlueprintDatabase : IDisposable
    {
        // Initialization completes before consumer threads start; disposal runs after they have all stopped.
        private static volatile BlueprintDatabase? fActiveInstance;
        private readonly string fName;
        private readonly string fConnectionString;
        private readonly HashSet<Type> fEntityTypes = new();
        private BlueprintDatabaseContext? fContext;
        private bool fModelChanged = true;
        private readonly IReadOnlyList<BlueprintLanguage> fLanguages;
        private readonly DatabaseTransactionFactory fTransactionFactory;
        private TimeSpan fRAMCacheRefreshInterval = TimeSpan.FromSeconds(1);
        private protected SqlEntityCache? fRAMCache;
        protected readonly object fSyncRoot = new();
        protected bool fIsDisposed;

        /// <summary>Stores validated settings and registers this instance as active without opening a connection.</summary>
        /// <param name="name">The database name.</param>
        /// <param name="connectionString">The connection settings.</param>
        /// <param name="languages">The model languages, with unique IDs and prefixes and exactly one language with an empty Prefix.</param>
        /// <remarks>The active instance is available during provider initialization. Each provider must dispose it if its constructor fails. Complete initialization before starting consumer threads. Dispose the active database before creating another.</remarks>
        /// <exception cref="ArgumentNullException">No language list was provided.</exception>
        /// <exception cref="ArgumentException">The settings or languages are invalid.</exception>
        /// <exception cref="InvalidOperationException">Another database is active.</exception>
        protected BlueprintDatabase(string name, string connectionString, List<BlueprintLanguage> languages)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            fName = name;
            fConnectionString = connectionString;
            fLanguages = LanguageColumns.ValidateLanguages(languages).AsReadOnly();
            if (fActiveInstance != null)
            {
                throw new InvalidOperationException("Another Blueprint database is active. Dispose it before creating another database.");
            }
            fActiveInstance = this;
         
            fTransactionFactory = new DatabaseTransactionFactory();
        }

        /// <summary>Executes provider-specific SQL on an independent connection without returning a result set.</summary>
        /// <param name="commandText">The SQL text in the provider's dialect; it must not be null, empty, or whitespace-only.</param>
        /// <param name="parameters">The ordered provider-compatible parameters, or null when no parameters are required.</param>
        /// <param name="commandTimeoutSeconds">The nonnegative command timeout in seconds; null uses the provider default and zero means no timeout.</param>
        /// <returns>The affected-row count reported by the provider, or -1 when a count is unavailable.</returns>
        /// <remarks>
        /// Implementations must open and dispose an independent connection for each call without using the shared entity context or holding the database lock during SQL execution.
        /// Calls from an existing database operation, including Blueprint transaction callbacks, or an ambient TransactionScope must be rejected; execution does not participate in those transactions.
        /// Local RAM caches must be invalidated after an execution attempt, including a failed attempt. Previously returned entities are not refreshed.
        /// </remarks>
        /// <exception cref="ArgumentException">The command text, parameters, or timeout are invalid for the provider.</exception>
        /// <exception cref="InvalidOperationException">Execution is requested from an existing database operation or an ambient transaction.</exception>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        /// <exception cref="DbException">The provider cannot open the connection or execute the command.</exception>
        public abstract int ExecuteNonQuery(string commandText, IReadOnlyList<DbParameter>? parameters = null, int? commandTimeoutSeconds = null);

        /// <summary>Registers a contract and immediately synchronizes the schema and optional cache.</summary>
        /// <typeparam name="TEntity">The class carrying EntityAttribute.</typeparam>
        /// <exception cref="ArgumentException">The contract is invalid.</exception>
        /// <exception cref="Exception">Provider synchronization fails without replacing the previously active model.</exception>
        public void RegisterEntity<TEntity>() where TEntity : BlueprintEntity
        {
            RegisterEntity(typeof(TEntity));
        }

        /// <summary>Registers an entity class and synchronizes the model, reverting the registration if the provider fails.</summary>
        /// <param name="entityType">The entity class.</param>
        /// <remarks>Registering the same type again resynchronizes the schema and cache without duplicates.</remarks>
        /// <exception cref="ArgumentNullException">No entity type was provided.</exception>
        /// <exception cref="ArgumentException">The type is not a valid contract.</exception>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        /// <exception cref="Exception">Synchronization or cache loading fails.</exception>
        public void RegisterEntity(Type entityType)
        {
            lock (fSyncRoot)
            {
                ObjectDisposedException.ThrowIf(fIsDisposed, this);
                ArgumentNullException.ThrowIfNull(entityType);
                BlueprintEntityTypes.Validate(entityType);
                bool wasModelChanged = fModelChanged;
                bool wasAdded = fEntityTypes.Add(entityType);
                fModelChanged |= wasAdded;
                try
                {
                    SynchronizeEntities();
                }
                catch
                {
                    if (wasAdded)
                    {
                        fEntityTypes.Remove(entityType);
                    }
                    fModelChanged = wasModelChanged;
                    throw;
                }
            }
        }

        /// <summary>Synchronizes the registered entity model and publishes the cache only after the provider succeeds.</summary>
        /// <exception cref="Exception">Schema synchronization or data loading fails.</exception>
        protected abstract void SynchronizeEntities();

        /// <summary>Creates an unsaved instance of the generated entity class.</summary>
        /// <typeparam name="TEntity">The registered entity class.</typeparam>
        /// <returns>The new entity with ID zero and CurrentLanguage set to the ID of the language with an empty Prefix.</returns>
        /// <exception cref="InvalidOperationException">This type has not been initialized.</exception>
        public TEntity Create<TEntity>() where TEntity : BlueprintEntity
        {
            return ExecuteOperation(context =>
            {
                var type = ResolveType<TEntity>(context);
                var entity = (TEntity)Activator.CreateInstance(type)!;
                foreach (var property in type.GetProperties())
                {
                    var related = global::Blueprint.Entities.BlueprintEntityTypes.GetRelatedContract(property.PropertyType);
                    if (related != null && related != property.PropertyType)
                    {
                        var listType = typeof(List<>).MakeGenericType(related);
                        if (property.PropertyType.IsAssignableFrom(listType))
                        {
                            property.SetValue(entity, Activator.CreateInstance(listType));
                        }
                    }
                }
                return entity;
            });
        }

        /// <summary>Creates a deferred query for a registered entity in the selected language.</summary>
        /// <typeparam name="TEntity">The registered entity class.</typeparam>
        /// <param name="language">The language ID, or null for the default language.</param>
        /// <returns>A composable query executed through the active database.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The language is not configured.</exception>
        /// <exception cref="InvalidOperationException">The entity is not registered.</exception>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        internal IQueryable<TEntity> Query<TEntity>(long? language = null) where TEntity : BlueprintEntity
        {
            return ExecuteOperation(context =>
            {
                long languageID = language ?? context.EntityTypes.DefaultLanguageID;
                _ = context.EntityTypes.getLanguage(languageID);
                _ = ResolveType<TEntity>(context);
                EntityQueryProvider provider = new EntityQueryProvider(typeof(TEntity), languageID);
                return new EntityQuery<TEntity>(provider);
            });
        }

        /// <summary>Creates write operations after validating the entity and selected language.</summary>
        /// <typeparam name="TEntity">The registered entity class.</typeparam>
        /// <param name="language">The language ID, or null for the default language.</param>
        /// <returns>The entity's SQL write operations.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The language is not configured.</exception>
        /// <exception cref="InvalidOperationException">The entity is not registered.</exception>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        internal BlueprintEntityCommands<TEntity> Commands<TEntity>(long? language = null) where TEntity : BlueprintEntity
        {
            return ExecuteOperation(context =>
            {
                long languageID = language ?? context.EntityTypes.DefaultLanguageID;
                _ = context.EntityTypes.getLanguage(languageID);
                _ = ResolveType<TEntity>(context);
                return new BlueprintEntityCommands<TEntity>(languageID);
            });
        }

        /// <summary>Executes an operation under a shared lock to prevent concurrent use of the DbContext.</summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="execute">The operation on the context.</param>
        /// <returns>The operation result.</returns>
        internal TResult ExecuteOperation<TResult>(Func<BlueprintDatabaseContext, TResult> execute)
        {
            lock (fSyncRoot)
            {
                return execute(Context);
            }
        }

        /// <summary>Finds an optional cached table under the database lock.</summary>
        /// <param name="contract">The registered entity class.</param>
        /// <returns>The cached table, or null when SQL is required.</returns>
        internal virtual RAMCachedTable? GetCachedTable(Type contract)
        {
            return null;
        }

        /// <summary>Synchronizes invalidated or expired cache snapshots before a supported read.</summary>
        /// <exception cref="Exception">Required cache synchronization fails.</exception>
        internal virtual void SynchronizeCache()
        {
        }

        /// <summary>Notifies the optional cache after a write operation under the database lock.</summary>
        internal virtual void InvalidateCache()
        {
        }

        /// <summary>Releases additional provider resources before closing the context, under the database lock.</summary>
        protected virtual void DisposeResources()
        {
        }

        /// <summary>Verifies that the entity class is included in the active model.</summary>
        /// <typeparam name="TEntity">The requested entity class.</typeparam>
        /// <param name="context">The active context.</param>
        /// <returns>The generated class.</returns>
        /// <exception cref="InvalidOperationException">The entity must be registered and initialized.</exception>
        internal static Type ResolveType<TEntity>(BlueprintDatabaseContext context) where TEntity : BlueprintEntity
        {
            return context.EntityTypes.Types.TryGetValue(typeof(TEntity), out Type? type) ? type : throw new InvalidOperationException($"Entity '{typeof(TEntity)}' is not registered. Call RegisterEntity() first.");
        }

        /// <summary>Builds the model and replaces the context only after successful schema initialization.</summary>
        /// <param name="options">The provider settings.</param>
        /// <param name="initialize">The database initialization operation.</param>
        /// <exception cref="InvalidOperationException">The model is being changed within a transaction.</exception>
        protected void InitializeContext(DbContextOptionsBuilder options, Action<DbContext> initialize)
        {
            lock (fSyncRoot)
            {
                ObjectDisposedException.ThrowIf(fIsDisposed, this);
                if (fContext?.Database.CurrentTransaction != null)
                {
                    throw new InvalidOperationException("Database initialization cannot run inside a transaction.");
                }
                if (fContext != null && !fModelChanged)
                {
                    initialize(fContext);
                    return;
                }
                options.ReplaceService<IModelCacheKeyFactory, EntityModelCacheKey>();
                BlueprintDatabaseContext candidate = new BlueprintDatabaseContext(options.Options, new BlueprintEntityTypes(fEntityTypes, fLanguages));
                try
                {
                    _ = candidate.Model;
                    initialize(candidate);
                    fContext?.Dispose();
                    fContext = candidate;
                    fModelChanged = false;
                }
                catch
                {
                    candidate.Dispose();
                    throw;
                }
            }
        }

        /// <summary>Releases database resources, clears this database's active registration, and prevents further operations.</summary>
        /// <remarks>Stop any independent background managers using this database before disposal. Repeated disposal is safe.</remarks>
        /// <exception cref="InvalidOperationException">Disposal is requested from inside a database operation.</exception>
        public void Dispose()
        {
            if (Monitor.IsEntered(fSyncRoot) && !fIsDisposed)
            {
                throw new InvalidOperationException("Dispose the database outside an existing database operation.");
            }
            lock (fSyncRoot)
            {
                if (fIsDisposed)
                {
                    return;
                }
                fIsDisposed = true;
                try
                {
                    DisposeResources();
                    fContext?.Dispose();
                    fContext = null;
                }
                finally
                {
                    Interlocked.CompareExchange(ref fActiveInstance, null, this);
                }
            }
        }

        /// <summary>The database registered by the base constructor, shared by static Entity API calls after provider initialization completes.</summary>
        /// <remarks>Callers must complete initialization before use and stop before disposal. No runtime null check is performed.</remarks>
        internal static BlueprintDatabase ACTIVE_INSTANCE
        {
            get
            {
                return fActiveInstance!;
            }
        }

        /// <summary>The shared lock protecting operations, transactions, and disposal for this instance.</summary>
        internal object SyncRoot
        {
            get
            {
                return fSyncRoot;
            }
        }

        /// <summary>The independent, read-only snapshot of languages configured when this database was created.</summary>
        public IReadOnlyList<BlueprintLanguage> Languages
        {
            get
            {
                return fLanguages;
            }
        }

        /// <summary>This database instance's sole factory for creating and executing transactions.</summary>
        public DatabaseTransactionFactory TransactionFactory
        {
            get
            {
                return fTransactionFactory;
            }
        }

        /// <summary>The database name.</summary>
        public string Name
        {
            get
            {
                return fName;
            }
        }

        /// <summary>The provider's connection settings.</summary>
        public string ConnectionString
        {
            get
            {
                return fConnectionString;
            }
        }

        /// <summary>The interval for checking external changes to loaded RAM-cached entities. Defaults to one second.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The interval is less than 1 ms or exceeds the timer limit.</exception>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        public TimeSpan RAMCacheRefreshInterval
        {
            get
            {
                lock (fSyncRoot)
                {
                    return fRAMCacheRefreshInterval;
                }
            }
            set
            {
                if (value < TimeSpan.FromMilliseconds(1) || value.TotalMilliseconds > uint.MaxValue - 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "The RAM cache refresh interval must fit the timer range and be at least one millisecond.");
                }
                lock (fSyncRoot)
                {
                    ObjectDisposedException.ThrowIf(fIsDisposed, this);
                    fRAMCacheRefreshInterval = value;
                    fRAMCache?.setRefreshInterval(value);
                }
            }
        }

        /// <summary>The successfully initialized context.</summary>
        internal BlueprintDatabaseContext Context
        {
            get
            {
                lock (fSyncRoot)
                {
                    ObjectDisposedException.ThrowIf(fIsDisposed, this);
                    return fContext ?? throw new InvalidOperationException("The database has not been initialized.");
                }
            }
        }
    }
}
