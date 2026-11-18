# Blueprint

<img src="Assets/BlueprintLogo.png" alt="Blueprint logo" width="120" align="left">

Blueprint is a backend application framework for .NET, **designed to provide a stable, consistent API across different underlying technologies**. Applications interact with Blueprint’s contracts and services, allowing infrastructure choices to evolve independently of business logic.

Database access follows this approach through `BlueprintDatabase`: applications select a database provider during initialization while keeping the same API for entities, queries, and transactions. Microsoft SQL Server is currently supported, with MySQL support planned.

Visit the [Blueprint Wiki](https://github.com/SourceRabbit/Blueprint/wiki) for setup instructions, documentation, and examples.

<br clear="left">

## Requirements

- .NET 10 SDK and C# 14.
- An accessible SQL Server instance.
- SQL permissions for data access and for database creation or schema updates when needed. RAM caching has [additional SQL requirements](.wiki/Database-RAMCaching.md#sql-server-requirements).

## Installation

Choose one of the following methods to reference Blueprint in your .NET 10 project.

### From GitHub Releases

To use [Blueprint 1.0.1](https://github.com/SourceRabbit/Blueprint/releases/tag/1.0.1) without downloading the DLL manually, right-click your project in Visual Studio and select **Edit Project File**. Add the following inside the existing `<Project>` element, before `</Project>`:

```xml
<PropertyGroup>
  <BlueprintVersion>1.0.1</BlueprintVersion>
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

Keep only one declaration of each package if your project already references it. The package versions above match Blueprint 1.0.1; a DLL reference alone does not install these dependencies.

Run **Build** once. The target downloads `Blueprint.dll` into `Libraries/Blueprint/1.0.1` before resolving references, and the reference copies it to your application's build output. Later builds reuse the downloaded file. Internet access is needed for the initial DLL download and any missing NuGet packages. IntelliSense may need that first build before it can resolve Blueprint types.

You can exclude `Libraries/Blueprint/` from source control because the DLL is downloaded when missing. To upgrade, set `BlueprintVersion` to the exact GitHub release tag and check the dependency versions required by that release. The example stays on the selected version rather than automatically following the latest release.

### From Source

Clone the repository and add a project reference to `Visual Studio Project/Blueprint/Blueprint.csproj` from your consuming project. This path is relative to the Blueprint repository root. The project reference builds Blueprint with your application and restores its NuGet dependencies. Use this method instead of the DLL reference above.

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

The English consumer manual is maintained in the [`.wiki` directory](.wiki/README.md) and published to the [GitHub Wiki](https://github.com/SourceRabbit/Blueprint/wiki). The `Sync Wiki` workflow publishes documentation changes pushed to `main`. It covers usage, examples, behavior, and limitations of the public API. For common errors, see [Troubleshooting](.wiki/Troubleshooting.md).
