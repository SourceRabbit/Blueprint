# Troubleshooting

Start with the exception and its inner exceptions, the operation being performed, and whether failure occurred during database construction, entity registration, querying, or transaction execution.

## Setup and model

| Symptom | Check and next step |
| --- | --- |
| Static methods such as `Product.Create()` are not found | Import `Blueprint.Entities`, use .NET 10 with C# 14, and reference the intended Blueprint assembly. |
| A previously valid API call no longer builds after replacing a DLL | Rebuild consumers against the same DLL and compatible dependencies. Transactions now use `database.TransactionFactory`; see [Transactions](Database-Transactions.md). |
| `SqlDatabase` construction fails | Check server reachability, authentication, certificate configuration, and permissions to access or create the named database. Construction connects immediately. |
| An entity cannot be created or queried | Ensure it is a public, non-sealed class deriving from `BlueprintEntityDB` or `BlueprintEntityRAM`, has `[Entity]`, is registered, and the database has initialized successfully. |
| An old interface or `[RAMCached]` declaration no longer compiles | Migrate the contract to a class with the appropriate DB/RAM base, add public abstract properties or implemented class properties, remove the cache annotation, and rebuild consumers. See [migration steps](Entitites-BlueprintEntity.md#migrating-existing-entities). |
| Class registration rejects a constructor or property | Supply a public or protected parameterless constructor, public mapped accessors, and abstract or virtual multilingual properties. Do not seal the class, hide inherited properties, or leave custom abstract methods unimplemented. |
| A second database cannot be activated | Keep one active database. Stop dependent background work and dispose the old database before switching. |
| Language validation fails | Supply a nonempty list with unique IDs and case-insensitively unique prefixes, exactly one empty prefix, and no null or whitespace-only prefixes. |
| Registration fails after adding a required field | Existing rows may need a default or explicit data migration. Blueprint does not invent required values. |
| Registration rejects a schema change | Check existing data, nulls, duplicate unique keys, narrowing conversions, and SQL permissions. See [Schema Synchronization](Database-SchemaSynchronization.md). |
| A relationship fails to register | Declare its scalar foreign key, verify `ForeignKey` and `InverseProperty` names, and use registered entity contracts for navigations. |

## Queries and persistence

| Symptom | Check and next step |
| --- | --- |
| Editing a loaded object does not save it | Call `Product.Update(product)` or enqueue `AddUpdate` and execute the transaction. There is no implicit save on property assignment. |
| A field or translation was overwritten unexpectedly | Updates write the object's mapped values. Load the current row before editing rather than constructing a partial object for an update. |
| A translation is null | Verify the configured language ID and that the translation was assigned and saved. Add `[FallbackToDefaultLanguage]` to a `[Multilingual]` property to read the default value for a null or empty translation; the default value may itself be null. |
| A query cannot translate to SQL | Remove unsupported C# method calls or `NotMapped` getters from the SQL portion; materialize a suitably filtered set before application-only logic. |
| `SingleOrDefault` throws | More than one row matched. Tighten the predicate, enforce the intended uniqueness, or use ordered `FirstOrDefault` when multiple matches are valid. |
| Cached and uncached string filters differ | In-memory comparisons may differ from SQL collation and null semantics. Review the predicate and caching choice. |
| Deleting a parent fails | Relationships restrict deletion while dependent rows exist. Delete or reassign dependents first. |
| IDs have gaps | Sequence values are not reclaimed after every rollback or deletion. Gaps are expected. |

## Transactions

| Symptom | Check and next step |
| --- | --- |
| Transaction reports that it was already submitted | Transactions are single-use even after failure or cancellation. Create a new transaction and reload state before retrying. |
| Transaction belongs to another database | Submit through the factory that created it. Ownership is by database instance, not by connection string. |
| Queued update or delete raises a concurrency exception | The row is missing or its concurrency token changed. Reload and resolve the conflict before constructing a new transaction. |
| Validation fails | The whole SQL transaction is rolled back when possible. Fix the business condition; do not reuse the submitted transaction. |
| Objects still contain edited values after rollback | Rollback restores the snapshot taken after preparation. It does not undo earlier consumer edits or arbitrary non-mapped state. |
| Execution is rejected from a callback | Submit outside the active database operation, transaction callback, or ambient `TransactionScope`. Nested execution is unsupported. |
| Cancellation is slower than the token's timeout | Cancellation does not interrupt lock waiting, synchronous SQL, or a running callback. It is checked between execution stages. |
| Failure includes an `AggregateException` | Inspect the original exception and the rollback/restoration failures. Verify durable state before deciding whether to retry. |

See [Transactions](Database-Transactions.md) for the full execution and restoration rules. Do not treat a cancelled token alone as proof that a transaction did not commit.

## RAM cache and background tasks

| Symptom | Check and next step |
| --- | --- |
| Cache startup fails | Verify Change Tracking, snapshot isolation, SQL permissions, and memory for all selected tables and translations. |
| Another application's update is not visible yet | Wait for a successful synchronization and run a new query. Already loaded objects are not refreshed. The polling interval is not a strict freshness bound. |
| Included objects show different freshness | Each target uses its own DB or RAM executor. SQL children can reflect recent writes while RAM parents or siblings still reflect a cached snapshot. Use a transaction when the whole graph must read SQL under one isolation scope. |
| A DB entity included by a RAM entity is not cached | This is intentional. `AutoInclude` requests loading and does not enable caching. Derive the target from `BlueprintEntityRAM` if its reads should use RAM. |
| Cached reads fail after a refresh error | Resolve the SQL or tracking problem. Reads retry synchronization and propagate continued failure. |
| Cache is inconsistent after database restore | Dispose the old database and initialize a new database instance. Change Tracking does not reliably identify replacement database backups. |
| Background task never runs | Keep the manager alive, call `Start()`, verify registration and schedule, and subscribe to `TaskFailed`. Interval tasks first run after their interval. |
| `RunTaskNow` returns false | The task is already running or being removed. The same task cannot overlap itself. |
| Task status is Idle after an error | Status expresses activity, not success. Use `TaskFailed` to record execution errors. |
| Manager Stop or Dispose waits for a long time | It waits for running tasks and callbacks. Keep synchronous task work bounded and avoid unobserved asynchronous work. |

## Reporting a reproducible problem

Include the Blueprint version or commit, target framework, a minimal entity contract, relevant language configuration, the failing API call, and the exception with inner exceptions. Remove credentials and private data from connection strings and logs. State whether RAM caching and concurrency tokens are enabled.

## Related topics

[Getting Started](GettingStarted.md) · [SQL Server](Database-SqlDatabase.md) · [Transactions](Database-Transactions.md) · [RAM Caching](Database-RAMCaching.md) · [Background Tasks](BackgroundTasks.md)
