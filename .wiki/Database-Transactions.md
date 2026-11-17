# Transactions

Use `database.TransactionFactory` to group inserts, updates, and deletes in one SQL transaction. Describe the operations with a `DatabaseTransaction`, then await `ExecuteTransactionAsync`. Blueprint commits when every operation and validation succeeds and attempts rollback on failure.

There are no consumer-managed begin, commit, or rollback steps. The factory is the public transaction entry point; the former synchronous `ExecuteTransaction(Action)` API has been removed.

## Public API

Import `Blueprint.Database.TransactionFactory` for `TransactionFactory` and `DatabaseTransaction`, and `Blueprint.Database.Entities` for the Entity API.

| Member | Purpose |
| --- | --- |
| `database.TransactionFactory` | Gets the factory owned by this database instance. |
| `CreateNewTransaction()` | Creates an empty transaction owned by this factory's database. |
| `CreateNewTransaction(Action<DatabaseTransaction> prepare)` | Creates a transaction whose synchronous preparation callback runs inside its SQL transaction. |
| `transaction.AddInsert(entity)` | Enqueues insertion of an unsaved entity with ID zero. |
| `transaction.AddUpdate(entity)` | Enqueues an update of an existing entity. |
| `transaction.AddDelete(entity)` | Enqueues deletion of an existing entity. |
| `transaction.AddValidation(Action)` | Registers a synchronous check after all writes and before commit. |
| `ExecuteTransactionAsync(transaction, isolationLevel, cancellationToken)` | Returns `Task<int>` with the total affected write rows after successful commit. Isolation defaults to `ReadCommitted`; cancellation is optional. |

## Insert, update, and delete together

This example assumes the [Product contract](Database-Tutorial-EShop-Checkout.md#define-a-contract), an active `database`, and two different existing IDs supplied by the application.

```csharp
using Blueprint.Database.Entities;
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

Operations run in registration order. Each entity must come from the active registered model. Updates and deletes require a positive ID and fail if the target row is missing. Navigation graphs are not saved implicitly; add every intended write. Insert parents before children, and delete children before parents. See [Relationships](Data-Entities-Relationships.md#insert-a-parent-and-child-together).

## Validate before commit

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

using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(addedProduct);
await database.TransactionFactory.ExecuteTransactionAsync(transaction, cancellationToken: cancellation.Token);
```

Cancellation is checked after obtaining the lock, after preparation, before operations and validations, and before commit. An observed cancellation prevents commit and attempts rollback.

Cancellation does not interrupt a running synchronous SQL command, a callback, or waiting to acquire the lock. A timeout token is therefore not a strict upper bound on execution time. Cancellation arriving after the final check can still be followed by a successful commit.

## RAM caching

Queries inside a transaction use SQL so they see its uncommitted writes. Blueprint invalidates cached state after execution; later cached reads synchronize committed data. A rollback must not publish rolled-back values into the cache. Previously returned objects remain independent and are not refreshed automatically.

## Related topics

[Queries and CRUD](Database-Queries-and-CRUD.md) · [Relationships](Data-Entities-Relationships.md) · [Data Annotations](Database-Entities-Annotations.md#concurrency) · [RAM Caching](Database-RAM-Caching.md)
