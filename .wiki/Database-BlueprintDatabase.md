# BlueprintDatabase

`BlueprintDatabase` is the **abstract base class** for Blueprint database providers, in the `Blueprint.Database` namespace. It implements `IDisposable` and manages configuration, entity registration, languages, transactions, and the active database used by the Entity API.

It cannot be instantiated directly. Create a concrete provider, such as `SqlDatabase`, and use it through a `BlueprintDatabase` reference. The base class implements shared behavior; the provider supplies database initialization, schema synchronization, and its storage-specific services.

## Providers

| Provider | Status | Documentation |
| --- | --- | --- |
| SQL Server (`SqlDatabase`) | Available. | [SQL Server](Database-SqlServer.md): connection settings, initialization, examples, and permissions. |
| MySQL | Coming Soon; not implemented yet. | [MySQL (Coming Soon)](Database-MySqlServer.md): reserved for MySQL configuration and usage. |

For a complete initialization example, see [SQL Server](Database-SqlServer.md#complete-example-from-entitiestutorial). MySQL support is planned; SQL Server configuration and caching requirements should not be treated as instructions for a future MySQL provider.

## Configuration

The protected base constructor receives a database name, a connection string, and a `List<BlueprintLanguage>`. It validates and stores the settings, captures an independent language configuration, and creates the instance's transaction factory. The base constructor does not open a database connection. The concrete provider initializes and activates the database before applications use the Entity API.

The language list must be nonempty, with unique IDs and case-insensitively unique prefixes. Exactly one language must have an empty prefix; that language is the default. Changing the original list after construction does not reconfigure the database. See [Multilingual Entities](Database-Multilingual-Entities.md) for the full language rules and translation behavior.

The database name, connection settings, and language configuration are read-only after construction. To change them, finish existing work, dispose the database, and initialize a new instance with the required configuration.

## Properties

| Property | Purpose |
| --- | --- |
| [Name](#name) | Gets the configured database name. |
| [ConnectionString](#connectionstring) | Gets the connection settings stored by the provider. |
| [Languages](#languages) | Gets the independent, read-only language configuration. |
| [TransactionFactory](#transactionfactory) | Gets this database instance's factory for creating and executing transactions. |
| [RAMCacheRefreshInterval](#ramcacherefreshinterval) | Gets or sets the polling interval for external changes to cached data. |

### Name

**Type:** `string`. **Access:** read-only.

The database name supplied during construction, such as `MyEshopDatabase`. It identifies the database used by this instance, not an entity or table. For `SqlDatabase`, this name overrides `Database` or `Initial Catalog` in the supplied connection string.

Read it through `database.Name`. To use another database name, dispose the current instance and construct a new provider with the desired name.

### ConnectionString

**Type:** `string`. **Access:** read-only.

The connection settings stored by the provider. `SqlDatabase` normalizes these settings to include the database selected by `Name`, so the returned string may differ from the original constructor argument.

Reading `database.ConnectionString` does not open a connection. The property has no setter; changing server, authentication, or other connection settings requires a new database instance. See [SQL Server connection settings](Database-SqlServer.md#database-name-and-constructor-behavior).

### Languages

**Type:** `IReadOnlyList<BlueprintLanguage>`. **Access:** read-only.

The independent language configuration captured during construction. Each `BlueprintLanguage` exposes a read-only `ID` and `Prefix`. The language with an empty prefix is the default, regardless of its ID or position in the list.

Use `database.Languages` to inspect the configured languages. Changing the list originally passed to the constructor does not change this snapshot or rebuild the model. A new language configuration requires a new database instance. See [Multilingual Entities](Database-Multilingual-Entities.md).

### TransactionFactory

**Type:** `Blueprint.Database.TransactionFactory.TransactionFactory`. **Access:** read-only.

The factory owned by this database instance for creating and executing `DatabaseTransaction` objects. Repeated reads of `database.TransactionFactory` return the same factory. Transactions created by it belong to this database instance.

Use `CreateNewTransaction()` to prepare queued writes and `ExecuteTransactionAsync(...)` to execute them with commit or rollback. Reading this property alone does not start a transaction. See [Transactions](Database-Transactions.md) for supported operations, preparation callbacks, validation, and examples.

### RAMCacheRefreshInterval

**Type:** `TimeSpan`. **Access:** read/write. **Default:** one second.

The polling interval for detecting external changes to RAM-cached data. It can be changed before or after entity registration; assigning a new value updates an existing cache timer. Values must be at least one millisecond and no greater than `4,294,967,294` milliseconds. An invalid value throws `ArgumentOutOfRangeException`; setting a valid value after disposal throws `ObjectDisposedException`.

For example, `database.RAMCacheRefreshInterval = TimeSpan.FromSeconds(20)` requests polling every twenty seconds. This does not delay the initial cache load or guarantee a maximum synchronization delay. It configures the optional cache rather than selecting which entities are cached. For the current SQL Server implementation, see [RAM Caching](Database-RAM-Caching.md).

## Methods

| Method | Purpose |
| --- | --- |
| [ExecuteNonQuery(commandText, parameters, commandTimeoutSeconds)](#executenonquery) | Abstract method for executing provider-specific SQL on an independent connection. |
| [RegisterEntity&lt;TEntity&gt;()](#registerentity) / [RegisterEntity(Type)](#registerentity) | Registers an entity contract and immediately invokes provider synchronization. |
| [Create&lt;TEntity&gt;()](#create) | Creates an unsaved entity; application code normally uses the entity's `Create()` method. |
| [Dispose()](#dispose) | Releases resources, clears this instance's active registration, and prevents new data operations. |

The examples below use the [Product contract from the checkout tutorial](Database-Tutorial-EShop-Checkout.md#define-a-contract). Except for the `Dispose` example, they assume an initialized database referenced by `database`. Register Product before creating or querying it. Examples are independent and use these imports as needed:

```csharp
using Blueprint.Database;
using Blueprint.Database.Entities;
using Blueprint.Database.Sql;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
```

### ExecuteNonQuery

`ExecuteNonQuery` is a **public abstract method** returning `int`. Each database provider implements command execution for its supported database. It executes SQL without returning a result set; the command text must use the selected provider's SQL dialect.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `commandText` | `string` | The SQL command text. It must not be null, empty, or whitespace-only. |
| `parameters` | `IReadOnlyList<DbParameter>?` | Optional ordered parameters compatible with the provider. Defaults to `null` when no parameters are needed. |
| `commandTimeoutSeconds` | `int?` | Optional command timeout in seconds. `null` uses the provider default, zero disables the timeout, and negative values are invalid. |

Parameters use the existing `System.Data.Common.DbParameter` base class. For example, `SqlDatabase` accepts `Microsoft.Data.SqlClient.SqlParameter` objects; a future provider supplies its own compatible parameter types. This shared API does not translate SQL dialects or convert parameter objects between providers.

The return value is the affected-row count reported by the provider. Zero is a successful result when no rows are affected; `-1` means a count is unavailable. The method does not return an inserted ID or entities. Execution failures propagate as exceptions rather than being represented by a row count.

Each call uses an independent connection, without using the shared entity context or holding the database lock during SQL execution. Calls from an existing database operation, including Blueprint transaction callbacks, or an ambient `TransactionScope` are rejected. Providers must invalidate local RAM caches after an execution attempt, including a failed attempt, so subsequent cached reads synchronize committed changes. Previously returned entities remain unchanged.

For example, with `database` initialized as a `SqlDatabase`, update the physical `dbo.Products` table using parameters:

```csharp
long productID = 123;
SqlParameter price = new SqlParameter("@price", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = 19.95m };
SqlParameter id = new SqlParameter("@id", SqlDbType.BigInt) { Value = productID };
int affectedRows = database.ExecuteNonQuery("UPDATE dbo.Products SET Price = @price WHERE ID = @id;", new DbParameter[] { price, id }, commandTimeoutSeconds: 30);
Console.WriteLine($"Affected rows: {affectedRows}");
```

Replace `123` with the intended existing product ID; an unmatched ID affects zero rows. Direct SQL does not add the entity's concurrency check or multilingual mapping. See [SQL Server: Execute SQL commands](Database-SqlServer.md#execute-sql-commands) for parameter ownership, cache synchronization, and lifetime behavior.

### RegisterEntity

Register a contract with `database.RegisterEntity<Product>()` or the equivalent `database.RegisterEntity(typeof(Product))`. A contract must be a public, non-sealed class deriving from `BlueprintEntityDB` or `BlueprintEntityRAM` and marked with `[Entity]`. See [BlueprintEntity](Database-Entities-BlueprintEntity.md) for contract definitions.

Both overloads return `void`. The generic overload takes the contract as `TEntity`; the other overload accepts an `entityType` parameter of type `Type`. A null or invalid contract is rejected, and registration after disposal throws `ObjectDisposedException`.

The base class validates and records the contract, then calls the protected abstract `SynchronizeEntities()` method implemented by the provider. Registration performs synchronization immediately; it does not defer initialization until the first query. Registering the same contract again synchronizes it again without creating a duplicate registration.

With SQL Server, synchronization updates the schema and loads the selected RAM-cached tables before registration returns. If synchronization fails, the newly added registration is removed and the previous active model and cache remain available. Provider configuration changes have their own rollback limits; see [Schema Synchronization](Database-Entities-Schema-Synchronization.md) and [RAM Caching](Database-RAM-Caching.md).

Use the generic overload when the contract is known at compile time:

```csharp
database.RegisterEntity<Product>();
```

Use the `Type` overload when the application holds the contract as a runtime type. This is an alternative to the previous call, not an additional required registration:

```csharp
Type entityType = typeof(Product);
database.RegisterEntity(entityType);
```

### Create

`Create<TEntity>()` returns a new, unsaved instance of the generated entity class implementing `TEntity`. It takes no value parameters; `TEntity` is the registered entity contract. The new entity starts with `ID = 0` and `CurrentLanguage` set to the default language's ID.

The method does not insert a row or save later property assignments. An unregistered contract causes `InvalidOperationException`; a disposed database causes `ObjectDisposedException`.

This public helper supports Blueprint's Entity API. The following shows the helper itself for reference:

```csharp
Product product = database.Create<Product>();
product.SKU = "CAMERA-NEW-001";
product.Price = 120m;
Console.WriteLine($"Unsaved product ID: {product.ID}");
```

In application code, use the entity's static `Create()` method, which delegates to the active database:

```csharp
Product product = Product.Create();
product.SKU = "CAMERA-NEW-001";
product.Price = 120m;
```

Both examples leave the product unsaved. Persist a fully populated entity explicitly through the Entity API or a queued transaction operation. See [Queries and CRUD](Database-Queries-and-CRUD.md).

### Dispose

`Dispose()` takes no parameters and returns `void`. It releases the provider's resources and the entity context, clears this instance's active registration, and prevents new data operations. Saved database rows are retained, and repeated disposal is safe.

Use a `using` block so disposal also occurs if registration or application work throws. This example owns its database lifetime and must run while no other Blueprint database is active:

```csharp
string connectionString = "Server=.;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
List<BlueprintLanguage> languages = new List<BlueprintLanguage> { new BlueprintLanguage(1, string.Empty) };
using (BlueprintDatabase database = new SqlDatabase("MyEshopDatabase", connectionString, languages))
{
    database.RegisterEntity<Product>();
    Console.WriteLine($"Database ready: {database.Name}");
}
```

Leaving the block calls `database.Dispose()` automatically. The connection string is a local development example; use the settings appropriate to your environment. This example initializes the named database and synchronizes Product during registration.

Stop background managers that use the database before disposal. For `SqlDatabase`, an independent `ExecuteNonQuery` call that already passed its lifetime check can finish after disposal; wait for outstanding calls before switching databases or shutting down. See [Application lifetime and concurrency](#application-lifetime-and-concurrency).

## Access data through the Entity API

Use `BlueprintDatabase` for configuration, registration, transactions, and lifetime management. Applications create and access data through entity methods such as `Product.Create()`, `Product.Where(...)`, `Product.Insert(...)`, and `Product.Update(...)`. Import `Blueprint.Database.Entities` to use these static extensions.

The public `Create<TEntity>()` helper supports Blueprint's implementation; application code uses the entity's `Create()` method. Query and command helpers are internal, and the former `Collection<TEntity>()` API has been removed. See [Queries and CRUD](Database-Queries-and-CRUD.md).

Editing an entity changes that object only. Persist changes explicitly through the Entity API or queue them in a transaction. There is no separate consumer `SaveChanges()` step.

## Application lifetime and concurrency

Keep one active `BlueprintDatabase` per process. Successful provider initialization activates it for static Entity API calls; creating another database while one is active throws. Failed initialization does not replace the active database.

Keep the instance alive while queries, writes, transactions, or background tasks use it. Deferred queries remain bound to their original database and require it when enumerated. Use a using block to ensure disposal even when registration or application work fails.

Stop or dispose independent `BackgroundTasksManager` instances that use the database before disposing the database itself. Database disposal does not stop those managers. See [Background Tasks](Background-Tasks.md) for the recommended lifetime arrangement.

Disposal calls the provider's resource-release hook, closes the context, and clears the active registration. Repeated disposal is safe. Operations tied to a disposed instance reject further use; static Entity API calls require a newly initialized database with the relevant contracts registered.

The base class coordinates database operations through a shared lock to protect its context. This does not make concurrent edits to returned entity objects safe or coordinate writers in other processes. Database isolation, constraints, and concurrency tokens govern those writes.

## Related topics

[SQL Server](Database-SqlServer.md) · [MySQL (Coming Soon)](Database-MySqlServer.md) · [Transactions](Database-Transactions.md) · [Getting Started](Getting-Started.md) · [User manual](README.md)
