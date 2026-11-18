# BlueprintEntity

`BlueprintEntity` is the abstract base class shared by every entity. It belongs to `Blueprint.Database.Entities` and defines `ID` and `CurrentLanguage`. Two abstract classes derive directly from it:

| Base class | Behavior |
| --- | --- |
| `BlueprintEntityDB` | Uses `QueryExecutor_Database` to read SQL, including when loaded as an `AutoInclude` of a RAM entity. |
| `BlueprintEntityRAM` | Uses `QueryExecutor_RAM` for eligible reads, including when loaded as an `AutoInclude` of a DB entity. Persists in SQL and synchronizes RAM through Change Tracking. |

Define a public, non-sealed class deriving from one of these two classes and mark it with `[Entity]`. The class can be abstract. Blueprint generates a concrete subclass when the contract is registered; continue to obtain instances through `Product.Create()` or the corresponding Entity API. Direct `new Product()` does not create the generated multilingual implementation and is not supported for persistence.

Both base classes use the same CRUD, transaction, translation, relationship, and concurrency APIs. Cache selection follows class inheritance, including intermediate base classes. The `[RAMCached]` annotation has been removed. See [RAM Caching](Database-RAM-Caching.md) for synchronization and `AutoInclude` dependencies.

Every automatically included entity selects its own executor. A DB parent can contain a RAM child and a RAM parent can contain a DB child. `AutoInclude` requests loading; it does not change the target's data source or enable caching for DB classes. See [Query Executors](Database-Query-Executors.md) for the hierarchy, shared LINQ API, and mixed-source relationships. Transaction reads use SQL for the entire graph.

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
    public abstract class CatalogItem : BlueprintEntityDB
    {
        public abstract string Code { get; set; }

        public abstract string Title { get; set; }
    }
}
```

`[Entity("CatalogItems")]` maps this contract to `dbo.CatalogItems`. The inherited `ID` becomes its primary key. `Code` and `Title` are ordinary stored properties; multilingual storage requires a `[Multilingual]` annotation.

Entity classes must be public, non-sealed, have no unbound generic parameters, and expose a public or protected parameterless constructor. Each registered class must declare its own `[Entity]`; an unregistered intermediate base class does not need it. Deriving directly from `BlueprintEntity` is not sufficient: choose `BlueprintEntityDB` or `BlueprintEntityRAM`.

Mapped properties require public getters and setters. Abstract properties are implemented by the generated subclass; ordinary implemented properties retain their accessors and initializers. `[Multilingual]` properties must have overridable accessors (`abstract` or non-sealed `virtual`/`override`), because Blueprint overrides them to select the current translation. Keep mapping attributes on the effective property declaration. Inherited properties and their annotations are supported; hiding an inherited property with `new` is rejected.

Keep mapped accessors free of side effects: they are used during persistence, materialization, and cached reads or copies. Multilingual accessor implementations are replaced by generated translation routing; assign initial translations through the generated instance or from the constructor, rather than through a multilingual auto-property initializer.

Class constructors run when generated instances are created, including during reads and cache copying. Keep constructors lightweight and free of external effects. The configured default language is set before the constructor runs, allowing a constructor to assign a virtual multilingual property. Implemented custom methods and computed `[NotMapped]` getters are supported; unimplemented abstract methods or events are rejected. See [Data Annotations](Database-Entities-Annotations.md).

Entity class inheritance does not introduce SQL inheritance tables or discriminator columns. Each registered contract retains its own table, columns, sequence, and relationships.

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

## Migrating existing entities

1. Replace `public interface Product : BlueprintEntity` with `public abstract class Product : BlueprintEntityDB`, or use `BlueprintEntityRAM` for a previously cached entity.
2. Remove `[RAMCached]` and add `public abstract` to the former interface properties. Alternatively use implemented class properties, keeping multilingual accessors virtual.
3. Preserve `[Entity]`, table and column names, annotations, and relationship types. Computed getters need an explicit `public` modifier.
4. Rebuild the library and all consumers. Interface-only inheritance must be adapted to C# single class inheritance; optional interfaces can still describe shared consumer behavior, while mapping annotations belong on class properties.

The Entity API calls stay the same. With unchanged names and mappings, this migration does not require renaming tables or columns or rewriting stored data. A RAM entity continues to write to SQL, and all transaction reads continue to bypass RAM.

## Related topics

[SQL Server](Database-SqlServer.md) · [Data Annotations](Database-Entities-Annotations.md) · [Relationships](Data-Entities-Relationships.md) · [Multilingual Entities](Database-Multilingual-Entities.md) · [EShop - Checkout Tutorial](Database-Tutorial-EShop-Checkout.md) · [User manual](README.md)
