# RAM Caching

`[RAMCached]` keeps a table's data in application memory for eligible Entity API queries. SQL Server remains the durable store. Writes still go to SQL, and SQL Server Change Tracking is used to synchronize committed changes made by this application or other writers.

## Enable caching

Add `[RAMCached]` to a contract before registration. For the shared Product example, add this annotation above its interface declaration:

```csharp
[RAMCached]
```

Import `Blueprint.Database.Entities.Annotations`. Set the refresh interval on the database when needed:

```csharp
database.RAMCacheRefreshInterval = TimeSpan.FromSeconds(2);
database.RegisterEntity<Product>();
```

Registration loads **every row and every translation** of every explicitly cached contract and every transitive `AutoInclude` dependency before returning. There is no lazy first-query loading, row limit, eviction policy, or automatic memory cap. A filtered query does not reduce the amount of data held by the cache. Choose tables whose complete contents fit your memory and startup budget.

The annotation is not inherited by other contracts. Full entities include private copies of cached references and collections in the requested language; cyclic paths stop expanding when a type repeats.

## Automatic caching of AutoInclude entities

**An entity marked `[RAMCached]` automatically makes all entities reached through its `[AutoInclude]` relationships RAM cached.** The related interfaces do not need their own `[RAMCached]` annotation. This applies to both references and collections and follows the entire chain of `AutoInclude` relationships.

For example, if `Product` has `[RAMCached]` and `Product.VatCategory` has `[AutoInclude]`, then `ProductVatCategory` is cached even when its interface does not declare `[RAMCached]`. Any further `AutoInclude` relationships on `ProductVatCategory` are cached automatically as well.

Blueprint enables Change Tracking for every participating table and synchronizes their snapshots together using the database's `RAMCacheRefreshInterval`. This is automatic cache configuration; it does not add attributes to the entity source files. Relationships without `[AutoInclude]` do not inherit caching from the parent entity.

## SQL Server requirements

Blueprint prepares the following settings when cached tables are registered:

- Database-level Change Tracking. If it is missing, Blueprint enables it with a two-day retention for Change Tracking metadata and automatic cleanup; existing configuration is preserved.
- Change Tracking on each cached table.
- `ALLOW_SNAPSHOT_ISOLATION`, used to read a consistent synchronization snapshot.

SQL Server periodically removes Change Tracking metadata older than the configured retention period. This metadata identifies which rows changed so Blueprint can synchronize its cache. **Automatic cleanup does not delete application data or rows from your database tables.**

The account needs the permissions for enabling those database and table settings when necessary, and permissions to read data and Change Tracking information, including `SELECT` and `VIEW CHANGE TRACKING` on tracked tables.

Database-level settings can be applied before the schema transaction and remain enabled if registration later fails. Removing `[RAMCached]` or shutting down the application does not disable these SQL features automatically.

The cache still needs SQL Server for synchronization and persistence. It is not an offline data store.

## Refresh interval and consistency

`RAMCacheRefreshInterval` defaults to one second and can be changed before or after registration. It must be at least one millisecond and fit the supported .NET timer range.

External writes become visible to new cached queries after a successful synchronization. The interval is a polling interval, not a maximum staleness guarantee: SQL work, database locking, and application load can delay a refresh. Each application instance maintains and refreshes its own cache.

Blueprint writes invalidate cached state so a subsequent cached read synchronizes it. Objects already returned to consumers remain independent and are not changed by a refresh.

## Running on multiple servers

When Blueprint runs on multiple servers connected to the same SQL Server database, each application instance keeps its own RAM cache and synchronization version. **Each instance independently reads SQL Server Change Tracking and updates its own cache from the shared database.** The servers do not need to exchange cache updates with one another.

For example, if a product's `Price` is changed and committed directly in the database, both Server A and Server B pick up that change during their next successful synchronization. Reading a change on one server does not consume it or prevent another server from reading it.

Each instance uses its own `RAMCacheRefreshInterval`, which defaults to one second. Instances can refresh at different times, so they are not guaranteed to see a change simultaneously. After synchronization, new cached queries see the updated value; entity objects already returned to callers remain unchanged.

## Concurrent updates

**RAM caching and SQL Server Change Tracking do not protect against lost updates from concurrent writers.** They synchronize committed data. Without a concurrency token or another explicit concurrency strategy, a later update can overwrite another writer's changes, even when the writers edited different properties, because Blueprint updates all mapped values.

To detect stale updates, declare a concurrency token on the entity contract. For SQL Server, add a property with `[Timestamp]` from `System.ComponentModel.DataAnnotations`:

```csharp
[Timestamp]
byte[] Version { get; set; }
```

SQL Server generates this `rowversion` value and changes it whenever the row is updated. Load the entity before editing it and preserve the token you read until saving; do not assign a new token yourself.

If Server A and Server B read the same version, and Server A saves first, Server B's update with the old token fails with `DbUpdateConcurrencyException` instead of overwriting Server A's changes. This check runs against SQL Server when saving, even if the entity was originally read from RAM cache.

The consumer must handle the conflict by reloading the current data and deciding how to merge or retry the intended changes, or by informing the user. Blueprint does not merge conflicting changes automatically. See [Data Annotations: Concurrency](Database-Entities-Annotations.md#concurrency) for details, including transaction handling.

## Queries and SQL fallback

Use the same Entity API as for uncached tables:

```csharp
List<Product> products = Product.Where(item => item.Price >= 50m).OrderBy(item => item.ID).ToList();
int count = Product.Where(item => item.Price >= 50m).Count();
```

Eligible queries use LINQ over an in-memory snapshot. This can differ from SQL semantics, especially case sensitivity, culture-sensitive comparisons, null handling, and database collation. Do not assume a string comparison behaves identically on cached and uncached contracts.

For explicit case/accent-insensitive title search, use [Contains with CompareOptions](Database-Queries-and-CRUD.md#contains-with-case-and-accent-options), for example `item.Title.Contains(search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)`, with `Blueprint.Database.Queries` and `System.Globalization` imported. The overload runs directly on immutable strings in RAM, including filters used by counts, paging, and projections. SQL fallback translates that same overload to an explicit case/accent collation only after cache routing; it does not force an otherwise eligible cached query onto SQL. Existing string overloads remain unchanged. See the linked page for null rules, supported options, and linguistic-comparison limits.

For `[Multilingual]` properties with `[FallbackToDefaultLanguage]`, both SQL and RAM reads use the default-language value when the selected translation is `null` or `""`. This also applies to cached filters, projections, counts, ordering, and paging. Whitespace-only translations are preserved. Cache snapshots retain the raw translations, so fallback does not change stored values or `CurrentLanguage`. See [Default-language fallback](Database-Multilingual-Entities.md#default-language-fallback).

Full entities with `AutoInclude` use cached relationship snapshots. Direct automatically included reference paths can also be used in filters, sorting and projections; included collection predicates can read their scalar fields. Queries use SQL when they require unsupported cache behavior, including relationships outside the loaded include paths, navigation after projections or inside nested collection lambdas, `EF.Functions`, unsupported entity member access, or another queryable root. Custom methods and `NotMapped` properties can also cause SQL fallback. Fallback is not a guarantee that SQL can translate the expression; simplify unsupported expressions or apply application logic after materialization.

Queries inside an application transaction always use SQL so they see its uncommitted writes.

## Independent returned objects

Normal queries return copies of entities, keeping consumer changes separate from cached values:

```csharp
Product product = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product was not found.");
product.Price = 135m;
Product.Update(product);
```

The assignment alone changes neither SQL nor the cache. An explicit update or queued transaction operation is required. Do not share the same mutable returned entity between concurrent writers.

## Counting, filtered lists, and single results

Eligible `Count` and `LongCount` queries over safe mapped properties can count without copying complete entities. Predicate captures, short-circuit behavior, and the selected language are preserved. Expressions that can mutate or expose entities, use mutable values, or have unsupported query shapes use protected copies or SQL instead. This optimization is not a promise of allocation-free predicate execution.

Eligible queries returning complete entities apply safe `Where` filters, ordering, and any `Skip`/`Take` operators to cached rows before copying the results. Paging is optional: `Product.Where(...).ToList()` copies only matching products, and ordered paging copies only the returned page:

```csharp
int pageIndex = 0;
int pageSize = 20;
List<Product> products = Product.Where(item => item.Price >= 50m)
    .OrderBy(item => item.Price)
    .ThenBy(item => item.ID)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToList();
```

`First`, `FirstOrDefault`, `Single`, and `SingleOrDefault` also select their result before copying, with or without a safe predicate. They return an independent entity with all its mapped data and translations. Empty `OrDefault` results remain null; `First`/`Single` still throw for missing results, and `Single`/`SingleOrDefault` still throw for multiple matches. The optimized path does not change the shared entity's language or return a shared mutable instance.

The complete table is still cached. These optimizations reduce temporary entity copies, not retained cache size, and do not add lookup indexes: a predicate may still scan cached rows. Use a stable sort for paging. Unsupported query shapes, explicit-default overloads, and filters that cannot safely read shared values retain the existing protected-copy or SQL execution path. All optimizations are automatic and require no additional consumer API.

## Selecting individual fields

Eligible `Select` queries read the requested mapped scalar fields directly from cached rows. They do not call the full-entity copy path or allocate a Product instance for each match. The result can be a scalar, an anonymous object, a tuple created with a constructor, or a new object initialized from safe field values:

```csharp
long[] ids = Product.Where(item => item.Price >= 50m).Select(item => item.ID).Distinct().ToArray();
(long ID, decimal? Price)[] prices = Product.Where(item => item.Price >= 50m)
    .Select(item => new ValueTuple<long, decimal?>(item.ID, item.Price))
    .ToArray();
```

Filters, ordering, and paging before `Select` operate on cached rows before creating the projected values. `Where`, ordering, `Distinct`, and integer `Skip`/`Take` after `Select` operate on those values in their original order. The selected language and `[FallbackToDefaultLanguage]` behavior are preserved without modifying the cached entity's `CurrentLanguage`.

The optimized path applies when enumerating a supported sequence, for example with `ToArray`, `ToList`, or `ToHashSet`. It does not expose the cached entity or mutable mapped fields: `Select(item => item)`, results containing an entity reference, mutable field values such as stored arrays, indexed selectors, multiple `Select` operations, and other unsupported expressions retain protected-copy or SQL execution. Terminal operations over projections also retain their existing execution path. Queries requiring relationships outside supported cached include paths, EF Core functions, other query roots, or an active transaction continue to use SQL.

This reduces per-query allocations and reads of unrequested fields. It does not change startup loading, retained cache contents, synchronization, or database storage.

## Refresh failures and lifecycle

Synchronization reads the Change Tracking version and changed rows within a consistent snapshot. Inserts and updates replace cached rows; deletes remove them. New snapshots are published together after successful synchronization, avoiding partially refreshed tables.

If change history is no longer usable, such as after retention expiry, tracking reinitialization, or `TRUNCATE`, Blueprint reloads the affected data. At the minimum valid version boundary it conservatively performs a full read.

If a background synchronization fails, no partial snapshot is published. The next cached read retries synchronization and propagates the error if it still fails; it does not silently continue serving a known failed refresh. Restore required SQL features if another administrator disabled them.

Re-registering contracts prepares the cache again. If model or cache preparation fails, the previous active model and cache are retained. `database.Dispose()` stops the cache timer and releases cached memory.

After restoring or replacing a database backup, dispose the old database and initialize a new database instance. Change Tracking is not a general database-backup identity detector.

## Transactions

Preparation and validation queries run against SQL under the transaction. Uncommitted data is not published to RAM. After commit or rollback, subsequent cached reads synchronize the committed state. The same database lock coordinates refreshes, ordinary queries, writes, and transaction execution.

## Related topics

[SQL Server](Database-SqlServer.md) · [Queries and CRUD](Database-Queries-and-CRUD.md) · [Transactions](Database-Transactions.md) · [Troubleshooting](Troubleshooting.md)
