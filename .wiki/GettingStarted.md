# Getting Started

This guide explains how to add Blueprint to your .NET project, either through a project reference or an existing DLL, and prepare the dependencies needed to build your application.

**Before writing any code, you must read [Contributing.md](Contributing.md) and follow its rules.** These conventions apply to library code, applications, tests, demos, and documentation examples.

## Requirements

- .NET 10 SDK and an application targeting `net10.0`.
- A C# 14-compatible compiler. Blueprint's static Entity API uses C# 14 extension members.
- An accessible SQL Server instance when running database operations.
- SQL permissions to access data and, when needed, create the database or synchronize its schema.

Blueprint currently provides SQL Server support through `SqlDatabase`. See [SQL Server](Database-SqlDatabase.md) for connection settings, registration, and application lifetime.

Entities are public, non-sealed classes derived from `BlueprintEntityDB` or `BlueprintEntityRAM`, with `[Entity]` on each registered class. Both persist in SQL; the RAM base also enables synchronized caching. Existing interface-based entities must be migrated and consumers rebuilt. See [entity class requirements and migration](Entitites-BlueprintEntity.md#migrating-existing-entities).

Import `Blueprint.Entities` for entity classes and their static API, and `Blueprint.Entities.Annotations` for Blueprint annotations. Consumers upgrading from the former entity namespaces must update their imports and rebuild; see [namespace migration](Entitites-BlueprintEntity.md#updating-namespace-imports).

## Prepare your project

Create a .NET application or open your existing project, and confirm that its target framework is .NET 10. Choose one of the following reference methods for the consuming project.

Use a **project reference** when you have the Blueprint source and want to build it with your application. Use a **DLL reference** when Blueprint is supplied as a compiled library. Keep one reference to Blueprint in each consuming project to avoid conflicting assembly versions.

## Add a project reference

1. Obtain a local checkout of the Blueprint repository.
2. Add the library project, `Visual Studio Project/Blueprint/Blueprint.csproj`, to your application's solution. This path is relative to the Blueprint repository root.
3. In the consuming project's references, add a project reference to the Blueprint library project.
4. Restore NuGet packages and build your application using your IDE or normal build workflow.

The project reference builds Blueprint with your application and brings its NuGet dependencies into the consuming project. You do not need to copy `Blueprint.dll` manually for this setup.

## Referencing a DLL

1. Obtain a `Blueprint.dll` built from the Blueprint version you intend to use. The current library targets .NET 10.
2. Place it in a stable location in your application repository, such as a `Libraries` directory.
3. Add an assembly reference from the consuming project to that DLL. Use a repository-relative reference path where possible and ensure the assembly is copied to the application's build output.
4. Add the required NuGet packages below to the consuming project, using its package manager.
5. Restore packages and rebuild the application. Ensure its output and deployment contain the intended Blueprint assembly and resolved dependencies.

A direct assembly reference does not automatically declare Blueprint's NuGet package dependencies. The following versions match the current [Blueprint project](../Visual%20Studio%20Project/Blueprint/Blueprint.csproj):

| NuGet package | Version |
| --- | --- |
| `Microsoft.Data.SqlClient` | `7.1.1` |
| `Microsoft.EntityFrameworkCore.SqlServer` | `10.0.14` |
| `System.Security.Cryptography.Pkcs` | `10.0.14` |

When replacing the DLL, check the dependency versions required by that library build and update the consuming project accordingly. Rebuild dependent projects after an API change and verify that their output directories contain the replacement DLL.

## Verify the setup

- Confirm that the consuming project targets `net10.0` and resolves the intended Blueprint reference.
- Restore packages and confirm that the application builds without missing assembly or package errors.
- Configure SQL Server access before running the examples. Adding the library reference alone does not initialize a database or activate Blueprint.

## Next steps

- [SQL Server](Database-SqlDatabase.md): configure the connection, register entities, and manage application lifetime.
- [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md): follow a complete example from user and basket creation to transactional checkout, order persistence, and stock reduction.
- [Transactions](Database-Transactions.md): group writes and validation with automatic commit or rollback.
- [Troubleshooting](Troubleshooting.md): resolve common setup, mapping, and persistence errors.
