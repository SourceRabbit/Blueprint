using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Blueprint.Database.Entities;
using Blueprint.Database.Queries;
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
        private static BlueprintDatabase? fActiveInstance;
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

        /// <summary>Stores database settings and an independent, validated language snapshot without opening a connection.</summary>
        /// <param name="name">The database name.</param>
        /// <param name="connectionString">The connection settings.</param>
        /// <param name="languages">The model languages, with unique IDs and prefixes and exactly one language with an empty Prefix.</param>
        /// <remarks>Each provider initializes and activates the database in its own constructor. Dispose the active database before creating another. Entities are then added through RegisterEntity.</remarks>
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
            if (Volatile.Read(ref fActiveInstance) != null)
            {
                throw new InvalidOperationException("Another Blueprint database is active. Dispose it before creating another database.");
            }
            fTransactionFactory = new DatabaseTransactionFactory(this, fSyncRoot);
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
        /// <typeparam name="TEntity">The interface carrying EntityAttribute.</typeparam>
        /// <exception cref="ArgumentException">The contract is invalid.</exception>
        /// <exception cref="Exception">Provider synchronization fails without replacing the previously active model.</exception>
        public void RegisterEntity<TEntity>() where TEntity : class, BlueprintEntity
        {
            RegisterEntity(typeof(TEntity));
        }

        /// <summary>Registers an interface and synchronizes the model, reverting the registration if the provider fails.</summary>
        /// <param name="entityType">The entity interface.</param>
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
        /// <typeparam name="TEntity">The registered interface.</typeparam>
        /// <returns>The new entity with ID zero and CurrentLanguage set to the ID of the language with an empty Prefix.</returns>
        /// <exception cref="InvalidOperationException">This type has not been initialized.</exception>
        public TEntity Create<TEntity>() where TEntity : class, BlueprintEntity
        {
            return ExecuteOperation(context =>
            {
                var type = ResolveType<TEntity>(context);
                var entity = (TEntity)Activator.CreateInstance(type)!;
                foreach (var property in type.GetProperties())
                {
                    var related = global::Blueprint.Database.Entities.BlueprintEntityTypes.GetRelatedContract(property.PropertyType);
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
        /// <typeparam name="TEntity">The registered entity interface.</typeparam>
        /// <param name="language">The language ID, or null for the default language.</param>
        /// <returns>A composable query bound to this database.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The language is not configured.</exception>
        /// <exception cref="InvalidOperationException">The entity is not registered.</exception>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        internal IQueryable<TEntity> Query<TEntity>(long? language = null) where TEntity : class, BlueprintEntity
        {
            return ExecuteOperation(context =>
            {
                long languageID = language ?? context.EntityTypes.DefaultLanguageID;
                _ = context.EntityTypes.getLanguage(languageID);
                _ = ResolveType<TEntity>(context);
                EntityQueryProvider provider = new EntityQueryProvider(this, typeof(TEntity), (currentContext, includeRelated) => currentContext.Query<TEntity>(includeRelated), languageID);
                return new EntityQuery<TEntity>(provider);
            });
        }

        /// <summary>Creates write operations after validating the entity and selected language.</summary>
        /// <typeparam name="TEntity">The registered entity interface.</typeparam>
        /// <param name="language">The language ID, or null for the default language.</param>
        /// <returns>The entity's SQL write operations.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The language is not configured.</exception>
        /// <exception cref="InvalidOperationException">The entity is not registered.</exception>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        internal BlueprintEntityCommands<TEntity> Commands<TEntity>(long? language = null) where TEntity : class, BlueprintEntity
        {
            return ExecuteOperation(context =>
            {
                long languageID = language ?? context.EntityTypes.DefaultLanguageID;
                _ = context.EntityTypes.getLanguage(languageID);
                _ = ResolveType<TEntity>(context);
                return new BlueprintEntityCommands<TEntity>(this, languageID);
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
        /// <param name="contract">The registered entity interface.</param>
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

        /// <summary>Verifies that the interface is included in the active model.</summary>
        /// <typeparam name="TEntity">The requested interface.</typeparam>
        /// <param name="context">The active context.</param>
        /// <returns>The generated class.</returns>
        /// <exception cref="InvalidOperationException">The entity must be registered and initialized.</exception>
        internal static Type ResolveType<TEntity>(BlueprintDatabaseContext context) where TEntity : class, BlueprintEntity
        {
            return context.EntityTypes.Types.TryGetValue(typeof(TEntity), out Type? type) ? type : throw new InvalidOperationException($"Entity '{typeof(TEntity)}' is not registered. Call RegisterEntity() first.");
        }

        /// <summary>Builds the model and activates the database only after successful schema initialization.</summary>
        /// <param name="options">The provider settings.</param>
        /// <param name="initialize">The database initialization operation.</param>
        /// <exception cref="InvalidOperationException">The model is being changed within a transaction or another database is active.</exception>
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
                    BlueprintDatabase? activeDatabase = Interlocked.CompareExchange(ref fActiveInstance, this, null);
                    if (activeDatabase != null && !ReferenceEquals(activeDatabase, this))
                    {
                        fContext = null;
                        throw new InvalidOperationException("Another Blueprint database is active. Dispose it before creating another database.");
                    }
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
        public void Dispose()
        {
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

        /// <summary>The successfully initialized database shared by static Entity API calls.</summary>
        /// <exception cref="InvalidOperationException">No initialized database is active.</exception>
        internal static BlueprintDatabase ACTIVE_INSTANCE
        {
            get
            {
                return Volatile.Read(ref fActiveInstance) ?? throw new InvalidOperationException("No active Blueprint database exists. Create an initialized database first.");
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

        /// <summary>The interval for checking external changes to loaded RAMCached entities. Defaults to one second.</summary>
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
