# Blueprint

<img src="Assets/BlueprintLogo.png" alt="Blueprint logo" width="120" align="left">

Blueprint is a backend application framework for .NET, **designed to provide a stable, consistent API across different underlying technologies**. Applications interact with Blueprint’s contracts and services, allowing infrastructure choices to evolve independently of business logic.

Database access follows this approach through `BlueprintDatabase`: applications select a database provider during initialization while keeping the same API for entities, queries, and transactions. Microsoft SQL Server is currently supported, with MySQL support planned.

Start with the [Getting Started guide](.wiki/Getting-Started.md) or browse the complete [Blueprint User Manual](.wiki/README.md).

<br clear="left">

## Requirements

- .NET 10 SDK and C# 14.
- An accessible SQL Server instance.
- SQL permissions for data access and for database creation or schema updates when needed. RAM caching has [additional SQL requirements](.wiki/Database-RAM-Caching.md#sql-server-requirements).

## Getting Started

Add a project reference to `Visual Studio Project/Blueprint/Blueprint.csproj`, define an entity contract, configure the database and languages, and register the contract. The successfully initialized database becomes active for Entity API calls automatically.

Once the [Product contract](.wiki/Database-Tutorial-EShop-Checkout.md#define-a-contract) is registered and `database` is active:

```csharp
using Blueprint.Database.Entities;
using Blueprint.Database.TransactionFactory;

Product product = Product.Create();
product.SKU = "CAMERA-001";
product.Title = "Camera";
product.Price = 120m;

DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(product);
await database.TransactionFactory.ExecuteTransactionAsync(transaction);
```

The [complete checkout example](.wiki/Database-Tutorial-EShop-Checkout.md#complete-example-in-main) includes the entity contracts, database initialization, user and basket creation, transactional checkout, stock updates, and shutdown. Editing a loaded entity does not save it automatically; persistence is explicit.

## Documentation

The English consumer manual is maintained in the [`.wiki` directory](.wiki/README.md) and published to the [GitHub Wiki](https://github.com/SourceRabbit/Blueprint/wiki). The `Sync Wiki` workflow publishes documentation changes pushed to `main`. It covers usage, examples, behavior, and limitations of the public API. For common errors, see [Troubleshooting](.wiki/Troubleshooting.md).
