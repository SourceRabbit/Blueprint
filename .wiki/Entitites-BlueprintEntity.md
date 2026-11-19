# BlueprintEntity

`BlueprintEntity` is the abstract base class for every entity in `Blueprint.Entities`. It defines `ID` and `CurrentLanguage`. Its two direct subclasses, `BlueprintEntityDB` and `BlueprintEntityRAM`, are abstract and share the same API for queries, writes, transactions, translations, relationships, and concurrency.

## Contents

- [Inherited properties](#inherited-properties)
- [BlueprintEntityDB](#blueprintentitydb)
- [BlueprintEntityRAM](#blueprintentityram)
- [Define an entity](#define-an-entity)
- [Register and create an instance](#register-and-create-an-instance)
- [JSON serialization](#json-serialization)
- [Method index](#method-index)
- [Queries and CRUD](#queries-and-crud)
- [Migrating existing entities](#migrating-existing-entities)
- [Related topics](#related-topics)

## Inherited properties

| Property | Type | Purpose |
| --- | --- | --- |
| `ID` | `long` | The primary key, stored as `bigint` in SQL Server. A new entity starts with zero; insertion assigns the generated ID to the same instance. |
| `CurrentLanguage` | `long` | The configured language ID used when reading or assigning multilingual properties. It is not stored as a database column. |

New instances use the language whose configured prefix is empty. The default language is determined by that prefix, not by a particular ID. See [Multilingual Entities](Entitites-MultilingualEntities.md) for language configuration and translated properties.

## BlueprintEntityDB

Derive from `BlueprintEntityDB` for SQL reads through `QueryExecutor_Database`. Inserts, updates, and deletes also execute in SQL. These entities are not added to the RAM cache when another entity includes them; each `AutoInclude` target uses its own executor.

The shared Entity API supports creation, LINQ queries, and writes. See [Queries and CRUD](#queries-and-crud) and [Query Executors](Database-QueryExecutors.md).

## BlueprintEntityRAM

Derive from `BlueprintEntityRAM` for eligible reads through `QueryExecutor_RAM`. SQL remains the persistent store, and SQL Server Change Tracking synchronizes committed changes into the RAM cache. Cache selection follows class inheritance, including intermediate base classes. This replaces the former `[RAMCached]` annotation.

Unsupported queries use SQL. See [RAM Caching](Database-RAMCaching.md) for refresh intervals, SQL requirements, and consistency.

Each `AutoInclude` target uses its own executor, so a DB parent can include a RAM child and a RAM parent can include a DB child. Inside a transaction, reads use SQL for the entire graph. See [Query Executors](Database-QueryExecutors.md).

## Define an entity

Define a public, non-sealed class deriving from `BlueprintEntityDB` or `BlueprintEntityRAM` and mark it with `[Entity]`. The class can be abstract. Blueprint generates a concrete subclass when the contract is registered; continue to obtain instances through `Product.Create()` or the corresponding Entity API. Direct `new Product()` does not create the generated multilingual implementation and is not supported for persistence.

Entity definitions, generated entity types, and the shared Entity API are organized under `Blueprint.Entities`, with public annotations in `Blueprint.Entities.Annotations`. SQL persistence stays under `Blueprint.Database.Sql`; its internal `BlueprintEntityCommands` handles inserts, updates, and deletes. This namespace separation preserves the existing SQL storage, RAM synchronization, and query behavior.

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

Changing a loaded object's properties does not save it automatically. Use `Update(...)` or a transaction to persist changes. See [Queries and CRUD](#queries-and-crud) and [Transactions](Database-Transactions.md) for reads and writes.

## JSON serialization

### Serialize an entity to JSON

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

### Create an entity from JSON

With `Blueprint.Entities` imported and `Product` registered in the active database, call `Product.FromJson(json)` to create the product in memory. `FromJson` restores the supplied translations by `LanguageID` and selects the configured default language. Omitted languages and fields keep their creation defaults. Explicit `null` and empty strings preserve the corresponding stored values. A supplied `ID` or foreign key is copied directly without fetching or saving a row.

Use the same case-sensitive property names as `ToJson()`. Importing `CurrentLanguage` or a translated field at the root raises a `JsonException`, as do duplicate or unknown language IDs, duplicate or unknown properties, and nested relationships. For multilingual contracts, `Translations` is reserved at the root and `LanguageID` is reserved inside translation entries.

#### Reading a fallback description after import

In the [product JSON above](#serialize-an-entity-to-json), English (`LanguageID = 1`) is the default language. Greek (`LanguageID = 2`) has a title and a `null` description. The `Product` class enables fallback on its `Description` property.

```csharp
[Multilingual, FallbackToDefaultLanguage]
public abstract string? Description { get; set; }
```

Run the example with the product JSON above, then change the Greek entry to `"Description": ""` and run it again. The code uses `System.Text.Json` and `System.Linq` to find the Greek entry by its language ID.

```csharp
Product product = Product.FromJson(json);
product.CurrentLanguage = 2;
Console.WriteLine(product.Title);       // Ασύρματα ακουστικά
Console.WriteLine(product.Description); // Over-ear headphones.

JsonElement translations = JsonElement.Parse(product.ToJson()).GetProperty("Translations");
JsonElement greek = translations.EnumerateArray()
    .Single(entry => entry.GetProperty("LanguageID").GetInt64() == 2);
Console.WriteLine(greek.GetProperty("Description").GetRawText());
```

| Greek `Description` in the input JSON | `product.Description` with Greek selected | Greek `Description` in `product.ToJson()` |
| --- | --- | --- |
| `null` | `"Over-ear headphones."` | `null` |
| `""` | `"Over-ear headphones."` | `""` |

The getter uses the English fallback for both values. Reading it leaves the stored Greek translation untouched, so `ToJson()` preserves the exact value supplied to `FromJson()`. See [Default-language fallback](Entitites-MultilingualEntities.md#default-language-fallback).

## Method index

**Entity API** means a static call on the registered contract, such as `Product.Where(...)`. **LINQ** means a call on the resulting `IQueryable<Product>`, such as `query.Count()`. Select a method to jump to its usage example.

### JSON methods

| JSON method | Purpose |
| --- | --- |
| [`product.ToJson()`](#serialize-an-entity-to-json) | Serialize scalar values and all stored translations. |
| [`Product.FromJson(json)`](#create-an-entity-from-json) | Create an unsaved product from its JSON representation. |

### Build a query

These methods compose a query without executing it.

| Method | Available through | Purpose |
| --- | --- | --- |
| [`Product.Where(predicate)`](#where) | Entity API and LINQ | Filter rows, optionally selecting a language through the Entity API. |
| [`query.Select(selector)`](#select) | LINQ | Project rows to the values needed by the consumer. |
| [`query.OrderBy(keySelector)`](#orderby-and-orderbydescending) | LINQ | Start an ascending ordering. |
| [`query.OrderByDescending(keySelector)`](#orderby-and-orderbydescending) | LINQ | Start a descending ordering. |
| [`orderedQuery.ThenBy(keySelector)`](#thenby-and-thenbydescending) | LINQ | Add an ascending tie-breaker to an existing ordering. |
| [`orderedQuery.ThenByDescending(keySelector)`](#thenby-and-thenbydescending) | LINQ | Add a descending tie-breaker to an existing ordering. |
| [`query.Skip(count)`](#skip-and-take) | LINQ | Skip rows before reading a page. |
| [`query.Take(count)`](#skip-and-take) | LINQ | Limit the number of returned rows. |

### Read results

These methods execute the composed query through the entity's executor, subject to the [query routing and SQL fallback rules](#query-limits-and-cached-entities).

| Method | Available through | Result |
| --- | --- | --- |
| [`Product.FirstOrDefault(predicate)`](#firstordefault) | Entity API and LINQ | The first matching entity, or null. |
| [`Product.SingleOrDefault(predicate)`](#singleordefault) | Entity API and LINQ | One matching entity, or null; multiple matches throw. |
| [`Product.Any(predicate)`](#any) | Entity API and LINQ | A `bool` indicating whether a match exists. |
| [`query.Count()`](#count-and-longcount) | LINQ | The number of matches as an `int`. |
| [`query.LongCount()`](#count-and-longcount) | LINQ | The number of matches as a `long`. |
| [`query.ToList()`](#tolist) | LINQ | A materialized `List<T>` of entities or projected values. |

### Create and write entities

`Create` only creates an object. `Insert`, `Update`, and `Delete` execute writes in SQL for both DB and RAM entities; RAM entities retain their [cache synchronization](Database-RAMCaching.md#refresh-interval-and-consistency) behavior.

| Method | Available through | Purpose |
| --- | --- | --- |
| [`Product.Create()`](#create) | Entity API | Create an unsaved instance of the generated entity class. |
| [`Product.Insert(product)`](#insert) | Entity API | Persist one entity or a collection; return the affected row count. |
| [`Product.Update(product)`](#update) | Entity API | Save mapped values for one entity or a collection; return the affected row count. |
| [`Product.Delete(predicate)`](#delete) | Entity API | Delete rows matching a predicate; return the affected row count. |

### Search text

These `TextSearch` string extensions can be used inside a query predicate or ordering expression. They do not execute the enclosing query by themselves.

| Method | Available through | Purpose |
| --- | --- | --- |
| [`text.Contains(value, options)`](#contains-with-case-and-accent-options) | String extension with `CompareOptions` | Match text anywhere in a string. |
| [`text.StartsWith(value, options)`](#startswith-with-case-and-accent-options) | String extension with `CompareOptions` | Match text at the beginning of a string. |

See also [language selection](#query-a-language), [transactions](#group-multiple-writes), and [query limits](#query-limits-and-cached-entities).

## Queries and CRUD

Use the static Entity API on registered contracts for reads and writes. Import `Blueprint.Entities` and `System.Linq`; initialize the database and register the contracts as shown in the [complete checkout example](Database-EShopCheckoutTutorial.md#complete-example-in-main).

The Entity API keeps the same signatures for reads and writes. The former `Collection<TEntity>` type and `database.Collection<TEntity>()` helpers have been removed. Use the Entity API for these operations, and import `Blueprint.Database.Queries` for `TextSearch` extensions.

Examples use the [Product contract](Database-EShopCheckoutTutorial.md#define-a-contract). `productID` represents an existing product ID supplied by your application. Run each write example independently and use unique SKUs when inserting again.

Both DB and RAM entity classes use this same LINQ syntax. Blueprint selects `QueryExecutor_Database` for `BlueprintEntityDB` and `QueryExecutor_RAM` for `BlueprintEntityRAM`. Every `AutoInclude` target selects its own executor independently of its parent, so results can contain both SQL and RAM entities. See [Query Executors](Database-QueryExecutors.md) for a release-year/price example and mixed-source behavior.

### Filter, order, and page

#### Where

Start with `Product.Where(predicate)` or `Product.Where(predicate, language)`. Both return `IQueryable<Product>`. Add more filters with LINQ's `query.Where(predicate)`; consecutive filters must all match.

```csharp
IQueryable<Product> query = Product.Where(item => item.Price >= 50m);
List<Product> products = query.Where(item => item.Stock > 0).OrderBy(item => item.ID).ToList();
```

Use `Product.Where(item => true)` when you need an unfiltered query to compose other operators. Select a language on the initial Entity API call, as shown in [Query a language](#query-a-language). A filter with no matches produces an empty sequence; the terminal method determines the final result.

#### Select

Use `query.Select(selector)` to return selected values instead of complete entities. The result is another query whose element type is the selector's result type.

```csharp
IQueryable<string> query = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID).Select(item => item.SKU);
List<string> skus = query.ToList();
```

Selecting a multilingual property uses the query's selected language and configured fallback. SQL projections must be translatable by the provider; see [query limits](#query-limits-and-cached-entities).

#### OrderBy and OrderByDescending

Use `query.OrderBy(keySelector)` for ascending order or `query.OrderByDescending(keySelector)` for descending order. Both return `IOrderedQueryable<Product>` when ordering a product query.

```csharp
IQueryable<Product> query = Product.Where(item => item.Price > 50m);
IOrderedQueryable<Product> cheapestFirst = query.OrderBy(item => item.Price);
IOrderedQueryable<Product> mostExpensiveFirst = query.OrderByDescending(item => item.Price);
```

Each `OrderBy` starts a new primary ordering. Use `ThenBy` or `ThenByDescending` to extend the existing ordering.

#### ThenBy and ThenByDescending

Use `orderedQuery.ThenBy(keySelector)` or `orderedQuery.ThenByDescending(keySelector)` to order rows that have equal preceding keys. These methods require an ordered query and return an ordered query that can accept further tie-breakers.

```csharp
List<Product> products = Product.Where(item => item.Price > 50m)
    .OrderBy(item => item.Price)
    .ThenByDescending(item => item.Stock)
    .ThenBy(item => item.ID)
    .ToList();
```

This sorts by price ascending, then by stock descending, then by ID ascending. An ID tie-breaker makes ordering deterministic when the other keys are equal.

#### Skip and Take

Use `query.Skip(count)` to skip rows and `query.Take(count)` to limit the result size. They return queries and do not read results until execution.

```csharp
List<Product> secondPage = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID).Skip(20).Take(20).ToList();
```

Order before paging. Skipping beyond the end produces an empty result; taking more rows than remain returns only the available rows. `Take(0)` produces an empty result. Use non-negative offsets and page sizes.

#### ToList

Use `query.ToList()` to execute a query and materialize its results as `List<T>`. No matches produce an empty list.

```csharp
IQueryable<Product> query = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID);
List<Product> products = query.ToList();
```

Further LINQ operations on `products` run in application memory. Call query operators before `ToList()` when they should be part of Blueprint's query execution. Calling `query.ToList()` again executes the query again; the existing list is not automatically refreshed.

#### Combined filtering and paging example

```csharp
using Blueprint.Entities;
using System.Linq;

decimal minimumPrice = 50m;
int pageIndex = 0;
int pageSize = 20;
List<Product> products = Product.Where(item => item.Price >= minimumPrice)
    .OrderBy(item => item.Price)
    .ThenBy(item => item.ID)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToList();
```

`Where` returns `IQueryable<Product>`. Execution happens when the query is enumerated or a terminal operation such as `ToList`, `Count`, or `Any` runs. Use a stable ordering, including an ID tie-breaker, for paging. Concurrent writes can still change offset-based pages between separate queries.

### Contains with case and accent options

Import `Blueprint.Database.Queries` and `System.Globalization` to use Blueprint's string extension:

```csharp
using Blueprint.Database.Queries;
using Blueprint.Entities;
using System.Globalization;
using System.Linq;

string search = "φορεμα";
List<Product> products = Product.Where(item => item.Title.Contains(search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace))
    .OrderBy(item => item.ID)
    .Skip(0)
    .Take(25)
    .ToList();
```

The new overload is `TextSearch.Contains(this string? source, string? value, CompareOptions options)`. It searches for a substring anywhere in the text. Existing `string.Contains(value)` and `string.Contains(value, StringComparison)` overloads keep their original behavior and translation support.

#### IgnoreCase: ignore uppercase and lowercase

```csharp
bool matchesCase = "Bodystocking".Contains("body", CompareOptions.IgnoreCase); // true
bool matchesGreekCase = "Φόρεμα".Contains("φόρεμα", CompareOptions.IgnoreCase); // true
bool matchesMissingAccent = "Φόρεμα".Contains("φορεμα", CompareOptions.IgnoreCase); // false

List<Product> products = Product.Where(item => item.Title.Contains("body", CompareOptions.IgnoreCase)).OrderBy(item => item.ID).Take(25).ToList();
```

`IgnoreCase` alone does not ignore accents. In particular, `StringComparison.OrdinalIgnoreCase` also does not ignore accents and is not a replacement for the combined options below.

#### IgnoreNonSpace: ignore accents, preserve case

```csharp
bool matchesAccent = "Φόρεμα".Contains("Φορεμα", CompareOptions.IgnoreNonSpace); // true
bool matchesDifferentCase = "Φόρεμα".Contains("φορεμα", CompareOptions.IgnoreNonSpace); // Runtime-dependent; see below.

List<Product> products = Product.Where(item => item.Title.Contains("Φορεμα", CompareOptions.IgnoreNonSpace)).OrderBy(item => item.ID).Take(25).ToList();
```

`IgnoreNonSpace` ignores diacritics such as Greek tonos. It also ignores other nonspacing diacritics, including diaeresis; it is not limited to the Greek acute accent. SQL uses a case-sensitive, accent-insensitive collation for this option. In memory, .NET linguistic comparison can also equate some case variants: the direct call above and the equivalent RAM filter returned true in the verified Windows/.NET environment, while the SQL filter returned false. Do not assume identical SQL/RAM case behavior for this option alone. Combine it with `IgnoreCase` when both case and accents should be ignored.

#### Combine the options

```csharp
CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
bool mixedCase = "Φόρεμα".Contains("φορεμα", options); // true
bool uppercase = "ΦΟΡΕΜΑ".Contains("φόρεμα", options); // true
bool insideTitle = "Καλοκαιρινό Φόρεμα".Contains("φορεμα", options); // true

string search = "φορεμα";
IQueryable<Product> query = Product.Where(item => item.Title.Contains(search, options), 1);
int totalCount = query.Count();
List<Product> page = query.OrderBy(item => item.ID).Skip(0).Take(25).ToList();
```

Replace language ID `1` with a configured language ID. The selected multilingual column and its existing default-language fallback are applied before matching. Filters remain inside the query before counting, sorting, and paging; no application-side loading of the complete product list is required.

#### Options, nulls, and SQL translation

The case and accent columns below describe the SQL Server collations. RAM uses .NET comparisons, with the differences noted below.

| Options | SQL case | SQL accents | SQL Server collation |
| --- | --- | --- | --- |
| `CompareOptions.None` | Sensitive | Sensitive | `Latin1_General_100_CS_AS_SC` |
| `CompareOptions.IgnoreCase` | Ignored | Sensitive | `Latin1_General_100_CI_AS_SC` |
| `CompareOptions.IgnoreNonSpace` | Sensitive | Ignored | `Latin1_General_100_CS_AI_SC` |
| `CompareOptions.IgnoreCase \| CompareOptions.IgnoreNonSpace` | Ignored | Ignored | `Latin1_General_100_CI_AI_SC` |

- These four combinations are supported. Other flags, including `IgnoreSymbols`, `Ordinal`, and `OrdinalIgnoreCase`, throw `ArgumentOutOfRangeException` rather than being silently ignored.
- If either `source` or `value` is null, the result is false. An empty search value matches every non-null source, including an empty source. These are explicit rules of this overload.
- `%`, `_`, and `[` are literal search characters, not caller-supplied SQL patterns. The EF Core provider handles SQL substring translation and escaping.
- In SQL queries, options must be constant or captured values independent of the current row. Blueprint evaluates them at execution to choose a fixed collation; row-dependent options are not supported.
- RAM queries execute `CultureInfo.InvariantCulture.CompareInfo.IndexOf` with the requested options. They do not depend on the current thread's culture and do not uppercase, strip accents from, or mutate stored titles.
- SQL queries translate only this overload to a null-safe string `Contains` using `EF.Functions.Collate` on the searched expression. The database and column collations are not changed. This applies to query enumeration, terminal operations, and `Product.Delete(predicate)`; delete continues to execute in SQL.
- .NET linguistic comparisons and SQL Server collations are separate implementations and can differ, including the Greek `IgnoreNonSpace` case above. Unicode equivalences and sorting-version changes can also affect matching. This is a linguistic search API, not an ordinal identity comparison.
- Explicit collation and arbitrary substring searches do not guarantee index seeks. This feature preserves the existing query routing and cache filtering before result copies; it does not introduce a full-text index.

See [RAM Caching](Database-RAMCaching.md#queries-and-sql-fallback), [.NET CompareOptions](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.compareoptions), and [EF Core collations](https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity).

### StartsWith with case and accent options

`TextSearch.StartsWith(this string? source, string? value, CompareOptions options)` checks only the beginning of the text. Import the same `Blueprint.Database.Queries` and `System.Globalization` namespaces used for `Contains`.

```csharp
bool ignoresCase = "Bodystocking".StartsWith("body", CompareOptions.IgnoreCase); // true
bool keepsAccent = "Φόρεμα".StartsWith("φορεμα", CompareOptions.IgnoreCase); // false
bool ignoresAccent = "Φόρεμα".StartsWith("Φορεμα", CompareOptions.IgnoreNonSpace); // true
bool matchesDifferentCase = "Φόρεμα".StartsWith("φορεμα", CompareOptions.IgnoreNonSpace); // Runtime-dependent, as with Contains.

CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
bool prefix = "Φόρεμα καλοκαιρινό".StartsWith("φορεμα", options); // true
bool insideTitle = "Καλοκαιρινό φόρεμα".StartsWith("φορεμα", options); // false
bool contains = "Καλοκαιρινό φόρεμα".Contains("φορεμα", options); // true
```

Use `Contains` to find the search text anywhere in a title. Use `StartsWith` in ordering when prefix matches should appear first without excluding the other matches:

```csharp
string search = "φορεμα";
CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
List<Product> products = Product.Where(item => item.Title.Contains(search, options))
    .OrderByDescending(item => item.Title.StartsWith(search, options))
    .ThenBy(item => item.Title)
    .ThenBy(item => item.ID)
    .Take(6)
    .ToList();
```

Both filtering and prefix ranking execute before `Take`. `StartsWith` uses `CultureInfo.InvariantCulture.CompareInfo.IsPrefix` in memory and a null-safe string `StartsWith` under the same explicit SQL Server collations as `Contains`. The supported options, null/empty rules, literal search characters, captured-options restriction, and linguistic-comparison limits described above also apply. Existing `string.StartsWith` overloads remain unchanged.

### Single rows and aggregates

#### FirstOrDefault

The Entity API provides `Product.FirstOrDefault()` and `Product.FirstOrDefault(predicate)`. LINQ also provides `query.FirstOrDefault()` and `query.FirstOrDefault(predicate)` on a composed query.

```csharp
Product? first = Product.FirstOrDefault(item => item.Price > 50m);
Product? cheapest = Product.Where(item => item.Price > 50m).OrderBy(item => item.Price).ThenBy(item => item.ID).FirstOrDefault();
```

For entity queries, no matches return null. Multiple matches are allowed; only the first row is returned. Without ordering, the chosen row is not guaranteed. The parameterless Entity API form considers all rows. There is no direct language overload; start with `Product.Where(predicate, language)` when needed.

#### SingleOrDefault

Use this when zero or one row is valid, and multiple matches should be treated as an error. The Entity API provides `Product.SingleOrDefault(long? language = null)` and `Product.SingleOrDefault(predicate, long? language = null)`. LINQ provides `query.SingleOrDefault()` and `query.SingleOrDefault(predicate)`.

```csharp
Product? product = Product.SingleOrDefault(item => item.ID == productID);
Product? translatedProduct = Product.SingleOrDefault(item => item.ID == productID, 2);
```

For entity queries, no matches return null; more than one match throws `InvalidOperationException`. `Product.SingleOrDefault()` checks all rows, so it throws if the table contains more than one row. Use a configured language ID for the optional language argument.

#### Any

The Entity API provides `Product.Any()` and `Product.Any(predicate)`. LINQ provides `query.Any()` and `query.Any(predicate)` on a composed query. All return `bool`.

```csharp
bool hasProducts = Product.Any();
bool exists = Product.Any(item => item.SKU == "CAMERA-001");
bool hasAffordableStock = Product.Where(item => item.Price < 200m).Any(item => item.Stock > 0);
```

No matches return false; one or more matches return true. Use `Any` when only existence matters, without materializing a list or requesting a count. There is no direct language overload; use a language-aware `Where` first.

#### Count and LongCount

These are LINQ terminal methods on a query, not static Entity API methods. Use `query.Count()` for an `int` result or `query.LongCount()` for a `long` result. Both also accept a predicate.

```csharp
IQueryable<Product> query = Product.Where(item => item.Price > 50m);
int count = query.Count();
long longCount = query.LongCount();
int inStockCount = query.Count(item => item.Stock > 0);
long inStockLongCount = query.LongCount(item => item.Stock > 0);
```

No matches return zero. Count before `Skip` and `Take` to obtain a total for pagination; counting a paged query counts only that page. Each terminal call executes separately, so a count and a subsequent page read can observe different data after concurrent writes.

#### Combined result examples

```csharp
Product? product = Product.SingleOrDefault(item => item.ID == productID);
Product? first = Product.FirstOrDefault(item => item.Price > 50m);
bool exists = Product.Any(item => item.SKU == "CAMERA-001");
int count = Product.Where(item => item.Price > 50m).Count();
List<string> skus = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID).Select(item => item.SKU).ToList();
```

`SingleOrDefault` returns null for no rows and throws when more than one row matches. `FirstOrDefault` permits multiple matches, but without ordering the chosen row is not guaranteed. Use `Where(...).OrderBy(...).FirstOrDefault()` when order matters.

The parameterless `FirstOrDefault()`, `SingleOrDefault()`, and `Any()` forms operate on all rows. `Count` and `LongCount` are standard LINQ terminal operations on a query.

### Query a language

```csharp
Product? frenchProduct = Product.SingleOrDefault(item => item.ID == productID, 2);
List<Product> frenchProducts = Product.Where(item => item.Title == "Appareil photo", 2).ToList();
Product? firstFrenchProduct = Product.Where(item => item.Price > 50m, 2).OrderBy(item => item.ID).FirstOrDefault();
```

The language argument is a configured `long` ID. It affects multilingual filters, projections, and the language selected on returned entities. Without an argument, queries use the configured language with an empty prefix. `FirstOrDefault` and `Any` do not have direct language overloads; use the language-aware `Where` form before the terminal operation.

### Create

`Product.Create()` returns an unsaved instance of Blueprint's generated entity class. Use it for both DB and RAM contracts, including abstract contracts.

```csharp
Product product = Product.Create();
product.SKU = "CAMERA-DRAFT";
product.Title = "Camera";
product.Price = 120m;
```

No row is inserted, and assigning properties does not save anything. Leave `ID` at zero and call `Product.Insert(product)` when the object is ready to persist.

### Insert

Use `Product.Insert(entity)` or `Product.Insert(IEnumerable<Product> entities)`. Both return the inserted row count as an `int`.

```csharp
Product product = Product.Create();
product.SKU = "CAMERA-001";
product.Title = "Camera";
product.Price = 120m;
int insertedRows = Product.Insert(product);
```

`Create` does not write to the database. Leave `ID` at zero; insertion assigns the generated ID to the same instance. An empty collection returns zero. SQL constraints, such as a duplicate value in a unique index, can cause insertion to fail.

### Update

Use `Product.Update(entity)` or `Product.Update(IEnumerable<Product> entities)`. Both return the affected row count as an `int`; an empty collection returns zero.

```csharp
Product product = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product was not found.");
product.Price = 135m;
int updatedRows = Product.Update(product);
```

The update writes mapped values, including translations. It is not an automatic dirty-field patch. Load the row first to preserve other values. `Update(IEnumerable<Product>)` accepts a collection. A direct update can return zero when the row does not exist; a queued transaction update treats a missing row as a concurrency failure.

### Delete

`Product.Delete(predicate)` returns the deleted row count as an `int`, or zero when nothing matches.

```csharp
int deletedRows = Product.Delete(item => item.ID == productID);
```

Delete uses the supplied predicate and can affect multiple rows. The static method does not take an entity instance. For a transaction, use `transaction.AddDelete(loadedProduct)` instead.

### Group multiple writes

Separate direct CRUD calls are not one shared atomic transaction. Use [TransactionFactory](Database-Transactions.md) when inserts, updates, deletes, and business checks must all succeed together. There is no public consumer `SaveChanges()` step: direct CRUD saves explicitly, and `ExecuteTransactionAsync` handles transaction commit or rollback.

### Query limits and cached entities

SQL queries must be translatable by the provider. Arbitrary C# methods and `NotMapped` getters do not automatically become SQL. Materialize a suitably filtered set before applying application-only logic.

For `BlueprintEntityRAM` contracts, eligible queries execute against in-memory snapshots; others use SQL. In-memory string comparisons can differ from the database collation. Returned entities remain independent objects. See [RAM Caching](Database-RAMCaching.md) for query routing and paging behavior.

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

[SQL Server](Database-SqlDatabase.md) · [Data Annotations](Entitites-DataAnnotations.md) · [Relationships](Entitites-Relationships.md) · [Multilingual Entities](Entitites-MultilingualEntities.md) · [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md) · [User manual](README.md) · [Transactions](Database-Transactions.md) · [RAM Caching](Database-RAMCaching.md)
