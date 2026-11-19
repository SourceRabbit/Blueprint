# BlueprintEntity

`BlueprintEntity` is the abstract base class shared by every entity. It belongs to `Blueprint.Entities` and defines `ID` and `CurrentLanguage`. Two abstract classes derive directly from it:

| Base class | Behavior |
| --- | --- |
| `BlueprintEntityDB` | Uses `QueryExecutor_Database` to read SQL, including when loaded as an `AutoInclude` of a RAM entity. |
| `BlueprintEntityRAM` | Uses `QueryExecutor_RAM` for eligible reads, including when loaded as an `AutoInclude` of a DB entity. Persists in SQL and synchronizes RAM through Change Tracking. |

Define a public, non-sealed class deriving from one of these two classes and mark it with `[Entity]`. The class can be abstract. Blueprint generates a concrete subclass when the contract is registered; continue to obtain instances through `Product.Create()` or the corresponding Entity API. Direct `new Product()` does not create the generated multilingual implementation and is not supported for persistence.

Both base classes use the same CRUD, transaction, translation, relationship, and concurrency APIs. Cache selection follows class inheritance, including intermediate base classes. The `[RAMCached]` annotation has been removed. See [RAM Caching](Database-RAMCaching.md) for synchronization and `AutoInclude` dependencies.

Every automatically included entity selects its own executor. A DB parent can contain a RAM child and a RAM parent can contain a DB child. `AutoInclude` requests loading; it does not change the target's data source or enable caching for DB classes. See [Query Executors](Database-QueryExecutors.md) for the hierarchy, shared LINQ API, and mixed-source relationships. Transaction reads use SQL for the entire graph.

Entity definitions, generated entity types, and the shared Entity API are organized under `Blueprint.Entities`, with public annotations in `Blueprint.Entities.Annotations`. SQL persistence stays under `Blueprint.Database.Sql`; its internal `BlueprintEntityCommands` handles inserts, updates, and deletes. This namespace separation preserves the existing SQL storage, RAM synchronization, and query behavior.

## Inherited properties

| Property | Type | Purpose |
| --- | --- | --- |
| `ID` | `long` | The primary key, stored as `bigint` in SQL Server. A new entity starts with zero; insertion assigns the generated ID to the same instance. |
| `CurrentLanguage` | `long` | The configured language ID used when reading or assigning multilingual properties. It is not stored as a database column. |

New instances use the language whose configured prefix is empty. The default language is determined by that prefix, not by a particular ID. See [Multilingual Entities](Entitites-MultilingualEntities.md) for language configuration and translated properties.

## Serialize an entity to JSON

Call `product.ToJson()` on an existing instance to get a JSON string with its current scalar values, including unsaved edits and null foreign keys. The method reads from memory and works after the database has been disposed. JSON keys use the public property names. A relationship contributes its foreign key, such as `BrandID`. References, collections, and computed `[NotMapped]` properties are omitted even when related entities are already loaded.

`ID` is always first. For a multilingual contract, `Translations` contains one object per configured language. Each object has a numeric `LanguageID` and every `[Multilingual]` field, using its stored value even when it is `null` or an empty string. Selecting another `CurrentLanguage` changes how the entity's getter displays a value and has no effect on the JSON.

```json
{
  "ID": 42,
  "BrandID": 7,
  "Translations": [
    {"LanguageID": 1, "Title": "Wireless headphones", "Description": "Over-ear headphones."},
    {"LanguageID": 2, "Title": "Ασύρματα ακουστικά", "Description": null}
  ]
}
```

A brand with a single ordinary `Name` property would produce `{"ID":7,"Name":"Acme"}`.

## Create an entity from JSON

With `Blueprint.Entities` imported and `Product` registered in the active database, pass the product JSON above to `Product.FromJson(json)`. In this example language 1 is the default and language 2 is Greek.

```csharp
Product product = Product.FromJson(json);
product.CurrentLanguage = 2;
string? greekTitle = product.Title;
```

`FromJson` restores the supplied translations by `LanguageID` and returns the entity with the configured default language selected. Omitted languages and fields keep their creation defaults. Explicit `null` and empty strings preserve the corresponding stored values. A supplied `ID` or foreign key is copied directly without fetching or saving a row.

Use the same case-sensitive property names as `ToJson()`. Importing `CurrentLanguage` or a translated field at the root raises a `JsonException`, as do duplicate or unknown language IDs, duplicate or unknown properties, and nested relationships. For multilingual contracts, `Translations` is reserved at the root and `LanguageID` is reserved inside translation entries.

## BlueprintEntityDB

Derive from `BlueprintEntityDB` for SQL reads through `QueryExecutor_Database`. Inserts, updates, and deletes also execute in SQL. These entities are not added to the RAM cache when another entity includes them; each `AutoInclude` target uses its own executor.

The shared Entity API supports creation, LINQ queries, and writes. See [Queries and CRUD](Database-QueriesAndCRUD.md) and [Query Executors](Database-QueryExecutors.md).

## BlueprintEntityRAM

Derive from `BlueprintEntityRAM` for eligible reads through `QueryExecutor_RAM`. SQL remains the persistent store, and SQL Server Change Tracking synchronizes committed changes into the RAM cache. Included entities continue to use their own DB or RAM source.

Unsupported queries and transaction reads use SQL. The consumer uses the same Entity API as DB entities. See [RAM Caching](Database-RAMCaching.md) for refresh intervals, SQL requirements, and consistency.

## Define an entity

```csharp
using Blueprint.Entities;
using Blueprint.Entities.Annotations;

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

Class constructors run when generated instances are created, including during reads and cache copying. Keep constructors lightweight and free of external effects. The configured default language is set before the constructor runs, allowing a constructor to assign a virtual multilingual property. Implemented custom methods and computed `[NotMapped]` getters are supported; unimplemented abstract methods or events are rejected. See [Data Annotations](Entitites-DataAnnotations.md).

Entity class inheritance does not introduce SQL inheritance tables or discriminator columns. Each registered contract retains its own table, columns, sequence, and relationships.

## Register and create an instance

Initialize the database as described in [SQL Server](Database-SqlDatabase.md). Inside its lifetime, register the contract before using its Entity API. The following statements assume an active `database` and run where `CatalogItem` is in scope, with `Blueprint.Entities` imported:

```csharp
database.RegisterEntity<CatalogItem>();

CatalogItem item = CatalogItem.Create();
item.Code = "CAMERA-001";
item.Title = "Camera";
CatalogItem.Insert(item);
```

Registration synchronizes the entity's schema and prepares any configured cache. `Create()` produces an unsaved instance; `Insert()` writes it to the database and fills its `ID`. Leave the ID at zero before insertion.

Calls such as `CatalogItem.Create()`, `CatalogItem.Where(...)`, and `CatalogItem.Insert(...)` are supplied by `BlueprintEntityQueries` through the `Blueprint.Entities` namespace. They use the active database and require the entity contract to be registered.

Changing a loaded object's properties does not save it automatically. Use `Update(...)` or a transaction to persist changes. See [Queries and CRUD](Database-QueriesAndCRUD.md) and [Transactions](Database-Transactions.md) for reads and writes.

## Migrating existing entities

### Updating namespace imports

Update consumer imports and fully qualified references, then rebuild the library and all consumers:

| Previous namespace | Current namespace |
| --- | --- |
| `Blueprint.Database.Entities` | `Blueprint.Entities` |
| `Blueprint.Database.Entities.Annotations` | `Blueprint.Entities.Annotations` |

The previous namespaces are no longer provided. Entity class names, static API calls, database tables, columns, and stored data stay unchanged. SQL configuration still uses `Blueprint.Database` and `Blueprint.Database.Sql`.

### Migrating interface contracts

1. Replace `public interface Product : BlueprintEntity` with `public abstract class Product : BlueprintEntityDB`, or use `BlueprintEntityRAM` for a previously cached entity.
2. Remove `[RAMCached]` and add `public abstract` to the former interface properties. Alternatively use implemented class properties, keeping multilingual accessors virtual.
3. Preserve `[Entity]`, table and column names, annotations, and relationship types. Computed getters need an explicit `public` modifier.
4. Rebuild the library and all consumers. Interface-only inheritance must be adapted to C# single class inheritance; optional interfaces can still describe shared consumer behavior, while mapping annotations belong on class properties.

The Entity API calls stay the same. With unchanged names and mappings, this migration does not require renaming tables or columns or rewriting stored data. A RAM entity continues to write to SQL, and all transaction reads continue to bypass RAM.

## Related topics

[SQL Server](Database-SqlDatabase.md) · [Data Annotations](Entitites-DataAnnotations.md) · [Relationships](Entitites-Relationships.md) · [Multilingual Entities](Entitites-MultilingualEntities.md) · [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md) · [User manual](README.md)
