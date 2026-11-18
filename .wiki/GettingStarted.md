# Getting Started

This guide explains how to add Blueprint 1.1.0 to your .NET project through a GitHub release, a project reference, or a downloaded DLL, and prepare the dependencies needed to build your application.

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

## Install from GitHub Releases

To use [Blueprint 1.1.0](https://github.com/SourceRabbit/Blueprint/releases/tag/1.1.0) with automatic DLL download, right-click the consuming project in Visual Studio and select **Edit Project File**. Add the following inside its existing `<Project>` element, before `</Project>`:

```xml
<PropertyGroup>
  <BlueprintVersion>1.1.0</BlueprintVersion>
  <BlueprintDirectory>$(MSBuildProjectDirectory)/Libraries/Blueprint/$(BlueprintVersion)</BlueprintDirectory>
</PropertyGroup>

<ItemGroup>
  <Reference Include="Blueprint">
    <HintPath>$(BlueprintDirectory)/Blueprint.dll</HintPath>
    <Private>true</Private>
  </Reference>

  <PackageReference Include="Microsoft.Data.SqlClient" Version="7.1.1" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.14" />
  <PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.14" />
</ItemGroup>

<Target Name="DownloadBlueprintRelease" BeforeTargets="ResolveAssemblyReferences" Condition="'$(DesignTimeBuild)' != 'true' and !Exists('$(BlueprintDirectory)/Blueprint.dll')">
  <DownloadFile SourceUrl="https://github.com/SourceRabbit/Blueprint/releases/download/$(BlueprintVersion)/Blueprint.dll" DestinationFolder="$(BlueprintDirectory)" DestinationFileName="Blueprint.dll" Retries="3" />
</Target>
```

Keep only one declaration of each package if your project already references it. Run **Build** once: the target downloads `Blueprint.dll` into `Libraries/Blueprint/1.1.0` before resolving references and copies it to the application's build output. Later builds reuse the downloaded file. Internet access is needed for the initial download and any missing NuGet packages; IntelliSense may need the first build to resolve Blueprint types.

The example pins Blueprint to `1.1.0`. To upgrade later, change `BlueprintVersion` to the intended release tag and check that its required dependency versions match your package references.

## Add a project reference

1. Obtain the source for [Blueprint 1.1.0](https://github.com/SourceRabbit/Blueprint/releases/tag/1.1.0), or check out the `1.1.0` tag in a local clone of the repository.
2. Add the library project, `Visual Studio Project/Blueprint/Blueprint.csproj`, to your application's solution. This path is relative to the Blueprint repository root.
3. In the consuming project's references, add a project reference to the Blueprint library project.
4. Restore NuGet packages and build your application using your IDE or normal build workflow.

The project reference builds Blueprint with your application and brings its NuGet dependencies into the consuming project. You do not need to copy `Blueprint.dll` manually for this setup.

## Referencing a DLL

1. Download [Blueprint.dll for 1.1.0](https://github.com/SourceRabbit/Blueprint/releases/download/1.1.0/Blueprint.dll) from the [1.1.0 release](https://github.com/SourceRabbit/Blueprint/releases/tag/1.1.0). This version targets .NET 10.
2. Place it in a stable location in your application repository, such as `Libraries/Blueprint/1.1.0/Blueprint.dll`.
3. Add an assembly reference from the consuming project to that DLL. Use a repository-relative reference path where possible and ensure the assembly is copied to the application's build output.
4. Add the required NuGet packages below to the consuming project, using its package manager.
5. Restore packages and rebuild the application. Ensure its output and deployment contain the intended Blueprint assembly and resolved dependencies.

A direct assembly reference does not automatically declare Blueprint's NuGet package dependencies. The following versions match Blueprint 1.1.0 and its [project file](../Visual%20Studio%20Project/Blueprint/Blueprint.csproj):

| NuGet package | Version |
| --- | --- |
| `Microsoft.Data.SqlClient` | `7.1.1` |
| `Microsoft.EntityFrameworkCore.SqlServer` | `10.0.14` |
| `System.Security.Cryptography.Pkcs` | `10.0.14` |

When replacing the DLL, check the dependency versions required by that library build and update the consuming project accordingly. Rebuild dependent projects after an API change and verify that their output directories contain the replacement DLL.

## Verify the setup

- Confirm that the consuming project targets `net10.0` and resolves Blueprint `1.1.0` through the chosen reference method.
- Restore packages and confirm that the application builds without missing assembly or package errors.
- Configure SQL Server access before running the examples. Adding the library reference alone does not initialize a database or activate Blueprint.

## Next steps

- [SQL Server](Database-SqlDatabase.md): configure the connection, register entities, and manage application lifetime.
- [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md): follow a complete example from user and basket creation to transactional checkout, order persistence, and stock reduction.
- [Transactions](Database-Transactions.md): group writes and validation with automatic commit or rollback.
- [Troubleshooting](Troubleshooting.md): resolve common setup, mapping, and persistence errors.
