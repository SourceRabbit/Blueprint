# Transactions

Use `database.TransactionFactory` to group inserts, updates, and deletes in one SQL transaction. Describe the operations with a `DatabaseTransaction`, then await `ExecuteTransactionAsync`. Blueprint commits when every operation and validation succeeds and attempts rollback on failure.

There are no consumer-managed begin, commit, or rollback steps. The factory is the public transaction entry point; the former synchronous `ExecuteTransaction(Action)` API has been removed.

## Public API

Import `Blueprint.Database.TransactionFactory` for `TransactionFactory` and `DatabaseTransaction`, and `Blueprint.Entities` for the Entity API.

Select a member to jump to its description and example. Creating a transaction or adding operations does not execute them; `ExecuteTransactionAsync` starts execution.

| Member | Purpose |
| --- | --- |
| [database.TransactionFactory](#transactionfactory) | Gets the factory owned by this database instance. |
| [CreateNewTransaction()](#createnewtransaction) | Creates an empty transaction owned by this factory's database. |
| [CreateNewTransaction(prepare)](#createnewtransaction-with-preparation) | Stores a synchronous callback that prepares operations inside the SQL transaction. |
| [new DatabaseTransaction()](#databasetransaction-constructor) | Creates an unbound transaction; the factory methods are the usual entry point. |
| [AddInsert(entity)](#addinsert) | Queues insertion of an unsaved entity with ID zero. |
| [AddUpdate(entity)](#addupdate) | Queues an update of an existing entity. |
| [AddDelete(entity)](#adddelete) | Queues deletion of an existing entity. |
| [AddValidation(validation)](#addvalidation) | Registers a synchronous check after all writes and before commit. |
| [ExecuteTransactionAsync(transaction, isolationLevel, cancellationToken)](#executetransactionasync) | Executes the transaction and returns the affected write count after commit. |
| [Operations](#operations) | Gets a read-only snapshot of queued CRUD operations. |
| [IsCompleted](#iscompleted) | Indicates whether submitted execution finished, including failure or cancellation. |
| [IsCommitted](#iscommitted) | Indicates whether SQL commit was confirmed. |

For execution rules, see [commit, rollback, and reuse](#commit-rollback-and-reuse), [thread safety and isolation](#thread-safety-and-isolation), [cancellation](#cancellation), and [RAM caching](#ram-caching).

## Reading the examples

Examples are independent and assume an initialized `BlueprintDatabase` named `database`, with the [Product contract](Database-EShopCheckoutTutorial.md#define-a-contract) registered on that active database. IDs such as `productID` are supplied by the application. Where an example uses `addedProduct` without creating it, supply a new product created through `Product.Create()`, with ID zero and its required values assigned. Use unique SKUs when inserting again.

Use these imports as needed:

```csharp
using Blueprint.Database;
using Blueprint.Database.TransactionFactory;
using Blueprint.Entities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
```

Reads performed before `ExecuteTransactionAsync` are outside the SQL transaction and can use RAM. Use the [preparation overload](#createnewtransaction-with-preparation) when reads must share the transaction's isolation.

## Create and execute transactions

### TransactionFactory

**Type:** `TransactionFactory`. **Access:** read-only property on `BlueprintDatabase`.

The factory belongs to one database instance and is reused when the property is read again. Reading it does not open a SQL transaction.

```csharp
TransactionFactory factory = database.TransactionFactory;
DatabaseTransaction transaction = factory.CreateNewTransaction();
```

Execute factory-created transactions through that same database instance, even when another instance connects to the same SQL database.

### CreateNewTransaction

`database.TransactionFactory.CreateNewTransaction()` returns an empty `DatabaseTransaction` bound to this database:

```csharp
DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
Console.WriteLine(transaction.Operations.Count); // 0
```

This requires an initialized, undisposed database. It does not begin a SQL transaction or save data. Add operations and validations, then submit the transaction once through `ExecuteTransactionAsync`.

### DatabaseTransaction constructor

The public parameterless constructor is also available. Unlike a factory-created transaction, this object is not bound to a database until its first accepted submission:

```csharp
DatabaseTransaction transaction = new DatabaseTransaction();
transaction.AddInsert(addedProduct);
await database.TransactionFactory.ExecuteTransactionAsync(transaction);
```

Prefer `CreateNewTransaction()` in normal consumer code so ownership is explicit from creation. Direct construction does not bypass model validation, single-use execution, or the requirement for an initialized database.

### ExecuteTransactionAsync

`ExecuteTransactionAsync(DatabaseTransaction transaction, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)` returns `Task<int>`.

```csharp
DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(addedProduct);
int affectedRows = await database.TransactionFactory.ExecuteTransactionAsync(transaction, isolationLevel: IsolationLevel.ReadCommitted, cancellationToken: CancellationToken.None);
Console.WriteLine($"Committed {affectedRows} writes.");
```

Execution opens a SQL transaction, runs preparation, executes queued writes in order, runs validations, and commits. Awaited success means commit was confirmed; the result counts affected write rows, not validation callbacks. Omitting the optional arguments uses `ReadCommitted` and no cancellation request.

A transaction can be submitted only once, including failed or cancelled executions. Failures propagate to the caller after any applicable rollback attempt. See [commit, rollback, and reuse](#commit-rollback-and-reuse), [cancellation](#cancellation), and the [worker-thread behavior](#thread-safety-and-isolation).

## Queue writes

`AddInsert`, `AddUpdate`, and `AddDelete` accept a `BlueprintEntity` and return `void`. They append entity references without saving or copying their values. Entities must belong to the registered model of the executing database. All three reject null; additions after submission are restricted to the transaction's preparation callback.

### AddInsert

Use `transaction.AddInsert(entity)` for an unsaved entity created through the Entity API:

```csharp
Product product = Product.Create();
product.SKU = "CAMERA-TRANSACTION-001";
product.Title = "Camera";
product.Price = 120m;

DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(product);
await database.TransactionFactory.ExecuteTransactionAsync(transaction);
Console.WriteLine(product.ID);
```

Leave `ID` at zero before insertion. SQL supplies the generated ID during execution; adding the operation alone leaves it unchanged. Insert referenced parents before their dependent entities. Navigation graphs are not inserted implicitly.

### AddUpdate

Use `transaction.AddUpdate(entity)` to save an existing entity's mapped values, including its translations:

```csharp
Product product = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product was not found.");
product.Price = 135m;

DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddUpdate(product);
await database.TransactionFactory.ExecuteTransactionAsync(transaction);
```

The entity needs a positive ID. Load it before editing to preserve other mapped values and concurrency tokens; the update is not a dirty-field patch. A missing row or a failed concurrency check causes execution to fail. The read above happens before transaction execution; use [preparation](#createnewtransaction-with-preparation) when the read must be inside it.

### AddDelete

Use `transaction.AddDelete(entity)` to delete a loaded entity by its ID, including any configured concurrency check:

```csharp
Product product = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product was not found.");

DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddDelete(product);
await database.TransactionFactory.ExecuteTransactionAsync(transaction);
```

This method takes an entity, not a predicate. It requires a positive ID and fails during execution if the row is missing or its concurrency token no longer matches. Queue dependent deletes before parent deletes; related entities are not deleted implicitly. See [Concurrency](Entitites-DataAnnotations.md#concurrency).

## Insert, update, and delete together

This example assumes the [Product contract](Database-EShopCheckoutTutorial.md#define-a-contract), an active `database`, and two different existing IDs supplied by the application.

```csharp
using Blueprint.Entities;
using Blueprint.Database.TransactionFactory;

Product addedProduct = Product.Create();
addedProduct.SKU = "CAMERA-NEW-001";
addedProduct.Title = "New camera";
addedProduct.Price = 120m;

Product updatedProduct = Product.SingleOrDefault(item => item.ID == updatedProductID) ?? throw new InvalidOperationException("Product to update was not found.");
updatedProduct.Price = 135m;
Product deletedProduct = Product.SingleOrDefault(item => item.ID == deletedProductID) ?? throw new InvalidOperationException("Product to delete was not found.");

DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(addedProduct);
transaction.AddUpdate(updatedProduct);
transaction.AddDelete(deletedProduct);
int affectedRows = await database.TransactionFactory.ExecuteTransactionAsync(transaction);
Console.WriteLine($"Committed {affectedRows} writes. New ID: {addedProduct.ID}");
```

The reads in this example happen **before** execution. When reads must share the transaction's isolation, use the preparation overload below.

`AddInsert`, `AddUpdate`, and `AddDelete` only register operations. They retain entity references, not copies of values at registration time. Finish editing before submission and do not change those objects while execution is pending.

Operations run in registration order. Each entity must come from the active registered model. Updates and deletes require a positive ID and fail if the target row is missing. Navigation graphs are not saved implicitly; add every intended write. Insert parents before children, and delete children before parents. See [Relationships](Entitites-Relationships.md#insert-a-parent-and-child-together).

## Validate before commit

### AddValidation

`transaction.AddValidation(Action validation)` returns `void` and stores a synchronous callback. The callback runs during execution, after all queued writes and before commit. Null callbacks and `async void` callbacks are rejected.

Add a validation that queries the final database state and throws when a business rule fails:

```csharp
DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(addedProduct);
transaction.AddValidation(() =>
{
    if (Product.Any(item => item.Price < 0m))
    {
        throw new InvalidOperationException("Product prices cannot be negative.");
    }
});

try
{
    await database.TransactionFactory.ExecuteTransactionAsync(transaction);
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    throw;
}
```

Here `addedProduct` is a new, unsaved product prepared by the application. Every validation runs **after all queued writes**, even if it was registered before an insert. Validations run in their own registration order. Their queries see the transaction's writes and bypass RAM caches.

A thrown exception prevents commit, skips later validations, and is propagated after the rollback attempt. Validation does not contribute to the returned affected-row count. An otherwise empty transaction can contain validations.

Callbacks must be synchronous. Use them for queries and checks; do not perform direct CRUD, change entities, start background work, or submit another transaction inside a validation. External effects such as an email or file write cannot be undone by SQL rollback. Perform those after successful execution.

## Prepare operations inside the transaction

### CreateNewTransaction with preparation

`database.TransactionFactory.CreateNewTransaction(Action<DatabaseTransaction> prepare)` returns a new transaction with a stored synchronous preparation callback. Null callbacks and `async void` callbacks are rejected.

Use this overload when deciding what to write requires reading current data under the same transaction and database lock:

```csharp
using System.Data;

DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction(pending =>
{
    Product product = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product was not found.");
    product.Price += 10m;
    pending.AddUpdate(product);
    pending.AddValidation(() =>
    {
        Product stored = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product disappeared.");
        if (stored.Price > 1000m)
        {
            throw new InvalidOperationException("The product price exceeds the allowed limit.");
        }
    });
});

await database.TransactionFactory.ExecuteTransactionAsync(transaction, IsolationLevel.Serializable);
```

The factory stores the callback; it does not run it immediately. Execution opens the SQL transaction, runs preparation, captures entity values for rollback, executes writes, validates, and commits.

Preparation may query, edit the entities it is preparing, and call `AddInsert`, `AddUpdate`, `AddDelete`, or `AddValidation`. Use these queued operations instead of direct CRUD. The callback is synchronous and must not start asynchronous work. Only the preparation thread may add operations after submission; concurrent callers cannot keep extending the transaction.

Use isolation appropriate to the rule. Putting reads inside a `ReadCommitted` transaction does not by itself protect every read-modify-write decision from other processes. The example uses `Serializable`; concurrency tokens can also detect stale writes.

## Inspect operations and outcome

### Operations

**Type:** `IReadOnlyList<DatabaseTransaction.Operation>`. **Access:** read-only.

Read the queued CRUD operations in registration order:

```csharp
DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(addedProduct);
IReadOnlyList<DatabaseTransaction.Operation> operations = transaction.Operations;
foreach (DatabaseTransaction.Operation operation in operations)
{
    Console.WriteLine($"{operation.Type}: entity ID {operation.Entity.ID}");
}
```

Each entry exposes these read-only properties:

| Property | Type | Meaning |
| --- | --- | --- |
| `Type` | `DatabaseTransaction.EOperationType` | The queued operation: `Insert`, `Update`, or `Delete`. |
| `Entity` | `BlueprintEntity` | The original entity reference supplied to the corresponding `Add` method. |

Each read returns a snapshot of the operation list. Previously obtained lists do not gain later additions, but the entity objects are shared references, not copies. Validations are not included. Operations added by a preparation callback appear only after that callback runs. Reading the list does not execute SQL; do not modify its entities while execution is pending.

### IsCompleted

**Type:** `bool`. **Access:** read-only.

This becomes true when an accepted execution finishes, including failure or cancellation. It starts false, so false alone cannot distinguish a new transaction from one still executing.

```csharp
DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(addedProduct);
try
{
    await database.TransactionFactory.ExecuteTransactionAsync(transaction);
}
finally
{
    Console.WriteLine($"Execution completed: {transaction.IsCompleted}");
}
```

Await the execution task to observe exceptions. `IsCompleted` describes completion, not success, and does not make a transaction reusable.

### IsCommitted

**Type:** `bool`. **Access:** read-only.

This is true only when the provider confirmed SQL commit:

```csharp
DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(addedProduct);
await database.TransactionFactory.ExecuteTransactionAsync(transaction);
Console.WriteLine($"Commit confirmed: {transaction.IsCommitted}");
```

Before execution it is false. A false value is not by itself proof of rollback, especially if the connection failed while committing. See [commit, rollback, and reuse](#commit-rollback-and-reuse) before retrying an uncertain outcome.

## Commit, rollback, and reuse

- Successful completion means SQL commit was confirmed. The returned integer counts affected writes, not validation checks.
- A write, preparation, validation, or observed cancellation failure causes a rollback attempt before the error is returned.
- `IsCompleted` indicates that execution ended, including failure. `IsCommitted` indicates a confirmed commit.
- Each transaction can be submitted only once, including failed or cancelled transactions. Build a new one for a retry.
- A factory-created transaction belongs to that exact database instance. Another instance cannot execute it, even if both use the same SQL database.
- `new DatabaseTransaction()` is also available; it binds to the database at its first accepted submission. Prefer the factory for normal application code.
- `Operations` is a read-only snapshot of queued CRUD descriptions. Its entries expose the original entities; it is not a deep copy and does not include validations.

Before writes, Blueprint snapshots the operations' mapped values. Rollback restores those values on the supplied objects, including generated IDs and foreign keys. The snapshot is taken **after preparation**: it does not undo edits made before submission or during preparation, and it does not restore arbitrary non-mapped state. Preserve application state separately if needed.

If rollback or object restoration also fails, an `AggregateException` preserves the original failure along with cleanup failures. A connection failure around commit can leave the outcome uncertain; check durable database state before retrying an operation that must not be duplicated.

## Thread safety and isolation

Execution holds the database's shared lock for preparation, writes, validation, commit or rollback, and cleanup. Other database operations on the same instance wait, so the shared context is not used concurrently. The lock does not make simultaneous consumer edits to the same entity safe.

Other processes and database instances are governed by SQL isolation, constraints, and concurrency tokens. The in-process lock does not serialize those writers.

Nested execution from inside a database operation or a transaction callback is rejected, as is submission within an ambient `TransactionScope`. Await execution from outside those operations. Do not block on work that needs the same database lock from a callback.

The current implementation runs synchronous SQL work on a worker task. `await` lets the caller wait asynchronously; it does not enable parallel SQL operations on the same database instance.

## Cancellation

```csharp
using System.Threading;

using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
{
    DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
    transaction.AddInsert(addedProduct);
    await database.TransactionFactory.ExecuteTransactionAsync(transaction, cancellationToken: cancellation.Token);
}
```

Cancellation is checked after obtaining the lock, after preparation, before operations and validations, and before commit. An observed cancellation prevents commit and attempts rollback.

Cancellation does not interrupt a running synchronous SQL command, a callback, or waiting to acquire the lock. A timeout token is therefore not a strict upper bound on execution time. Cancellation arriving after the final check can still be followed by a successful commit.

## RAM caching

Queries inside a transaction use SQL so they see its uncommitted writes. Blueprint invalidates cached state after execution; later cached reads synchronize committed data. A rollback must not publish rolled-back values into the cache. Previously returned objects remain independent and are not refreshed automatically.

This applies to both `BlueprintEntityDB` and `BlueprintEntityRAM`, including their `AutoInclude` relationships. Preparation and validation queries execute inside this SQL scope. A query run before submission remains outside it and can read from RAM normally.

## Related topics

[Queries and CRUD](Entitites-BlueprintEntity.md#queries-and-crud) · [Relationships](Entitites-Relationships.md) · [Data Annotations](Entitites-DataAnnotations.md#concurrency) · [RAM Caching](Database-RAMCaching.md)
