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

### Download from GitHub Releases

1. Open the [latest Blueprint release](https://github.com/SourceRabbit/Blueprint/releases/latest). Under **Assets**, download `Blueprint.dll` and `Blueprint.pdb`.
2. Place both files in the same folder, for example `Libraries/Blueprint/`. The PDB contains the debugging symbols for the matching DLL.
3. In Visual Studio, right-click your project's **Dependencies**, select **Add Project Reference**, then **Browse**, and select `Blueprint.dll`.
4. Install the NuGet dependencies listed for the same Blueprint version under **Dependencies** on the [package page](https://www.nuget.org/packages/SourceRabbit.Blueprint#dependencies-body-tab), then build your project.

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
