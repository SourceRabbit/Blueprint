# Getting Started

Install Blueprint in your .NET 10 project from NuGet or download the DLL and its debugging symbols from the latest GitHub release.

**Before writing any code, you must read [Contributing.md](Contributing.md) and follow its rules.** These conventions apply to library code, applications, tests, demos, and documentation examples.

## Requirements

- .NET 10 SDK and an application targeting `net10.0`.
- A C# 14-compatible compiler. Blueprint's static Entity API uses C# 14 extension members.
- An accessible SQL Server instance when running database operations.
- SQL permissions to access data and, when needed, create the database or synchronize its schema.

Blueprint currently provides SQL Server support through `SqlDatabase`. See [SQL Server](Database-SqlDatabase.md) for connection settings, registration, and application lifetime.

Entities are public, non-sealed classes derived from `BlueprintEntityDB` or `BlueprintEntityRAM`, with `[Entity]` on each registered class. Both persist in SQL; the RAM base also enables synchronized caching. Existing interface-based entities must be migrated and consumers rebuilt. See [entity class requirements and migration](Entitites-BlueprintEntity.md#migrating-existing-entities).

Import `Blueprint.Entities` for entity classes and their static API, and `Blueprint.Entities.Annotations` for Blueprint annotations. Consumers upgrading from the former entity namespaces must update their imports and rebuild; see [namespace migration](Entitites-BlueprintEntity.md#updating-namespace-imports).

## Installation

Choose one of the following methods to add Blueprint to your .NET 10 project.

### From NuGet

From your project directory, install [SourceRabbit.Blueprint](https://www.nuget.org/packages/SourceRabbit.Blueprint):

```shell
dotnet package add SourceRabbit.Blueprint
```

In Visual Studio, right-click your project, select **Manage NuGet Packages**, then search the **Browse** tab for `SourceRabbit.Blueprint` and select **Install**. NuGet restores the package and its dependencies.

### Download from GitHub Releases

1. Open the [latest Blueprint release](https://github.com/SourceRabbit/Blueprint/releases/latest). Under **Assets**, download both `Blueprint.dll` and `Blueprint.pdb` from that release.
2. Place both files in the same folder in your application repository, for example `Libraries/Blueprint/`. The PDB contains debugging symbols for the matching DLL.
3. In Visual Studio, right-click your project's **Dependencies**, select **Add Project Reference**, then **Browse**, and select `Blueprint.dll`.
4. On the [SourceRabbit.Blueprint package page](https://www.nuget.org/packages/SourceRabbit.Blueprint), select the version that matches the downloaded release. Open **Dependencies** and install the listed packages in your application.
5. Build your project and confirm that its output folder contains `Blueprint.dll`.

## Verify the setup

- Confirm that the consuming project targets `net10.0` and resolves the Blueprint version you installed.
- Restore packages and confirm that the application builds without missing assembly or package errors.
- Configure SQL Server access before running the examples. Adding the library reference alone does not initialize a database or activate Blueprint.

## Next steps

- [SQL Server](Database-SqlDatabase.md): configure the connection, register entities, and manage application lifetime.
- [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md): follow a complete example from user and basket creation to transactional checkout, order persistence, and stock reduction.
- [Transactions](Database-Transactions.md): group writes and validation with automatic commit or rollback.
- [Troubleshooting](Troubleshooting.md): resolve common setup, mapping, and persistence errors.
