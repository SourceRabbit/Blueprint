# Blueprint

<img src="Assets/BlueprintLogo.png" alt="Blueprint logo" width="120" align="left">

Blueprint is a backend application framework for .NET, **designed to provide a stable, consistent API across different underlying technologies**. Applications interact with Blueprint’s contracts and services, allowing infrastructure choices to evolve independently of business logic.

Database access follows this approach through `BlueprintDatabase`: applications select a database provider during initialization while keeping the same API for entities, queries, and transactions. Microsoft SQL Server is currently supported, with MySQL support planned.

See the [Blueprint documentation](https://github.com/SourceRabbit/Blueprint/tree/main/.wiki) for setup instructions, API details, and examples.

<br clear="left">

## Requirements

- .NET 10 SDK and C# 14.
- An accessible SQL Server instance.
- SQL permissions for data access and for database creation or schema updates when needed. RAM caching has [additional SQL requirements](.wiki/Database-RAMCaching.md#sql-server-requirements).

## Installation

Choose one of the following methods to reference Blueprint in your .NET 10 project.

### From NuGet

Install [SourceRabbit.Blueprint](https://www.nuget.org/packages/SourceRabbit.Blueprint) from your project directory:

```shell
dotnet package add SourceRabbit.Blueprint
```

In Visual Studio, right-click your project, select **Manage NuGet Packages**, then search the **Browse** tab for `SourceRabbit.Blueprint` and select **Install**.

### From Source

Clone the repository and add a project reference to `Visual Studio Project/Blueprint/Blueprint.csproj` from your consuming project. This path is relative to the Blueprint repository root. The project reference builds Blueprint with your application and restores its NuGet dependencies.

See the [Getting Started guide](.wiki/GettingStarted.md) for more details about project references and manually supplied DLLs.

## Getting Started

After adding a Blueprint reference, define an entity contract, configure the database and languages, and register the contract. The successfully initialized database becomes active for Entity API calls automatically.

Once the [Product contract](.wiki/Database-EShopCheckoutTutorial.md#define-a-contract) is registered and `database` is active:

```csharp
using Blueprint.Entities;
using Blueprint.Database.TransactionFactory;

Product product = Product.Create();
product.SKU = "CAMERA-001";
product.Title = "Camera";
product.Price = 120m;

DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(product);
await database.TransactionFactory.ExecuteTransactionAsync(transaction);
```

The [complete checkout example](.wiki/Database-EShopCheckoutTutorial.md#complete-example-in-main) includes the entity contracts, database initialization, user and basket creation, transactional checkout, stock updates, and shutdown. Editing a loaded entity does not save it automatically; persistence is explicit.

## Documentation

The English consumer manual is maintained in the [`.wiki` directory](https://github.com/SourceRabbit/Blueprint/tree/main/.wiki) and published to the [GitHub Wiki](https://github.com/SourceRabbit/Blueprint/wiki). The `Sync Wiki` workflow publishes documentation changes pushed to `main`. It covers usage, examples, behavior, and limitations of the public API. For common errors, see [Troubleshooting](.wiki/Troubleshooting.md).
