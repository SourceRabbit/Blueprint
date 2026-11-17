# BlueprintEntity

`BlueprintEntity` is the base interface for entity contracts in Blueprint. It belongs to the `Blueprint.Database.Entities` namespace and defines the properties shared by every entity.

Define a public interface that inherits from `BlueprintEntity` and mark it with `[Entity]`. Blueprint generates the concrete implementation when the contract is registered with the database. Use the contract's `Create()` method to obtain an instance.

## Inherited properties

| Property | Type | Purpose |
| --- | --- | --- |
| `ID` | `long` | The primary key, stored as `bigint` in SQL Server. A new entity starts with zero; insertion assigns the generated ID to the same instance. |
| `CurrentLanguage` | `long` | The configured language ID used when reading or assigning multilingual properties. It is not stored as a database column. |

New instances use the language whose configured prefix is empty. The default language is determined by that prefix, not by a particular ID. See [Multilingual Entities](Database-Multilingual-Entities.md) for language configuration and translated properties.

## Define an entity

```csharp
using Blueprint.Database.Entities;
using Blueprint.Database.Entities.Annotations;

namespace BlueprintManual
{
    /// <summary>Describes a catalog item with a code and title.</summary>
    [Entity("CatalogItems")]
    public interface CatalogItem : BlueprintEntity
    {
        string Code { get; set; }

        string Title { get; set; }
    }
}
```

`[Entity("CatalogItems")]` maps this contract to `dbo.CatalogItems`. The inherited `ID` becomes its primary key. `Code` and `Title` are ordinary stored properties; multilingual storage requires a `[Multilingual]` annotation.

Entity contracts must be public interfaces without unbound generic parameters. Ordinary mapped properties need getters and setters. Contracts cannot declare methods or events. See [Data Annotations](Database-Entities-Annotations.md) for mapping, validation constraints, and computed properties excluded from storage.

## Register and create an instance

Initialize the database as described in [SQL Server](Database-SqlServer.md). Inside its lifetime, register the contract before using its Entity API. The following statements assume an active `database` and run where `CatalogItem` is in scope, with `Blueprint.Database.Entities` imported:

```csharp
database.RegisterEntity<CatalogItem>();

CatalogItem item = CatalogItem.Create();
item.Code = "CAMERA-001";
item.Title = "Camera";
CatalogItem.Insert(item);
```

Registration synchronizes the entity's schema and prepares any configured cache. `Create()` produces an unsaved instance; `Insert()` writes it to the database and fills its `ID`. Leave the ID at zero before insertion.

Calls such as `CatalogItem.Create()`, `CatalogItem.Where(...)`, and `CatalogItem.Insert(...)` are supplied by `BlueprintEntityQueries` through the `Blueprint.Database.Entities` namespace. They use the active database and require the entity contract to be registered.

Changing a loaded object's properties does not save it automatically. Use `Update(...)` or a transaction to persist changes. See [Queries and CRUD](Database-Queries-and-CRUD.md) and [Transactions](Database-Transactions.md) for reads and writes.

## Related topics

[SQL Server](Database-SqlServer.md) · [Data Annotations](Database-Entities-Annotations.md) · [Relationships](Data-Entities-Relationships.md) · [Multilingual Entities](Database-Multilingual-Entities.md) · [EShop - Checkout Tutorial](Database-Tutorial-EShop-Checkout.md) · [User manual](README.md)
