# Queries and CRUD

Use the static Entity API on registered contracts for reads and writes. Import `Blueprint.Entities` and `System.Linq`; initialize the database and register the contracts as shown in the [complete checkout example](Database-EShopCheckoutTutorial.md#complete-example-in-main).

The Entity API keeps the same signatures for reads and writes. The former `Collection<TEntity>` type and `database.Collection<TEntity>()` helpers have been removed. Use the Entity API for these operations, and import `Blueprint.Database.Queries` for `TextSearch` extensions.

Examples use the [Product contract](Database-EShopCheckoutTutorial.md#define-a-contract). `productID` represents an existing product ID supplied by your application. Run each write example independently and use unique SKUs when inserting again.

Both DB and RAM entity classes use this same LINQ syntax. Blueprint selects `QueryExecutor_Database` for `BlueprintEntityDB` and `QueryExecutor_RAM` for `BlueprintEntityRAM`. Every `AutoInclude` target selects its own executor independently of its parent, so results can contain both SQL and RAM entities. See [Query Executors](Database-QueryExecutors.md) for a release-year/price example and mixed-source behavior.

## Method index

**Entity API** means a static call on the registered contract, such as `Product.Where(...)`. **LINQ** means a call on the resulting `IQueryable<Product>`, such as `query.Count()`. The tables cover the methods explained on this page; they are not a list of every LINQ operator or overload supported by a provider.

### Build a query

These methods compose a query without executing it.

| Method | Available through | Purpose |
| --- | --- | --- |
| [Where](#where) | Entity API and LINQ | Filter rows, optionally selecting a language through the Entity API. |
| [Select](#select) | LINQ | Project rows to the values needed by the consumer. |
| [OrderBy](#orderby-and-orderbydescending) | LINQ | Start an ascending ordering. |
| [OrderByDescending](#orderby-and-orderbydescending) | LINQ | Start a descending ordering. |
| [ThenBy](#thenby-and-thenbydescending) | LINQ | Add an ascending tie-breaker to an existing ordering. |
| [ThenByDescending](#thenby-and-thenbydescending) | LINQ | Add a descending tie-breaker to an existing ordering. |
| [Skip](#skip-and-take) | LINQ | Skip rows before reading a page. |
| [Take](#skip-and-take) | LINQ | Limit the number of returned rows. |

### Read results

These methods execute the composed query through the entity's executor, subject to the [query routing and SQL fallback rules](#query-limits-and-cached-entities).

| Method | Available through | Result |
| --- | --- | --- |
| [FirstOrDefault](#firstordefault) | Entity API and LINQ | The first matching entity, or null. |
| [SingleOrDefault](#singleordefault) | Entity API and LINQ | One matching entity, or null; multiple matches throw. |
| [Any](#any) | Entity API and LINQ | A `bool` indicating whether a match exists. |
| [Count](#count-and-longcount) | LINQ | The number of matches as an `int`. |
| [LongCount](#count-and-longcount) | LINQ | The number of matches as a `long`. |
| [ToList](#tolist) | LINQ | A materialized `List<T>` of entities or projected values. |

### Create and write entities

`Create` only creates an object. `Insert`, `Update`, and `Delete` execute writes in SQL for both DB and RAM entities; RAM entities retain their [cache synchronization](Database-RAMCaching.md#refresh-interval-and-consistency) behavior.

| Method | Available through | Purpose |
| --- | --- | --- |
| [Create](#create) | Entity API | Create an unsaved instance of the generated entity class. |
| [Insert](#insert) | Entity API | Persist one entity or a collection; return the affected row count. |
| [Update](#update) | Entity API | Save mapped values for one entity or a collection; return the affected row count. |
| [Delete](#delete) | Entity API | Delete rows matching a predicate; return the affected row count. |

### Search text

These `TextSearch` string extensions can be used inside a query predicate or ordering expression. They do not execute the enclosing query by themselves.

| Method | Available through | Purpose |
| --- | --- | --- |
| [Contains](#contains-with-case-and-accent-options) | String extension with `CompareOptions` | Match text anywhere in a string. |
| [StartsWith](#startswith-with-case-and-accent-options) | String extension with `CompareOptions` | Match text at the beginning of a string. |

See also [language selection](#query-a-language), [transactions](#group-multiple-writes), and [query limits](#query-limits-and-cached-entities).

## Filter, order, and page

### Where

Start with `Product.Where(predicate)` or `Product.Where(predicate, language)`. Both return `IQueryable<Product>`. Add more filters with LINQ's `query.Where(predicate)`; consecutive filters must all match.

```csharp
IQueryable<Product> query = Product.Where(item => item.Price >= 50m);
List<Product> products = query.Where(item => item.Stock > 0).OrderBy(item => item.ID).ToList();
```

Use `Product.Where(item => true)` when you need an unfiltered query to compose other operators. Select a language on the initial Entity API call, as shown in [Query a language](#query-a-language). A filter with no matches produces an empty sequence; the terminal method determines the final result.

### Select

Use `query.Select(selector)` to return selected values instead of complete entities. The result is another query whose element type is the selector's result type.

```csharp
IQueryable<string> query = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID).Select(item => item.SKU);
List<string> skus = query.ToList();
```

Selecting a multilingual property uses the query's selected language and configured fallback. SQL projections must be translatable by the provider; see [query limits](#query-limits-and-cached-entities).

### OrderBy and OrderByDescending

Use `query.OrderBy(keySelector)` for ascending order or `query.OrderByDescending(keySelector)` for descending order. Both return `IOrderedQueryable<Product>` when ordering a product query.

```csharp
IQueryable<Product> query = Product.Where(item => item.Price > 50m);
IOrderedQueryable<Product> cheapestFirst = query.OrderBy(item => item.Price);
IOrderedQueryable<Product> mostExpensiveFirst = query.OrderByDescending(item => item.Price);
```

Each `OrderBy` starts a new primary ordering. Use `ThenBy` or `ThenByDescending` to extend the existing ordering.

### ThenBy and ThenByDescending

Use `orderedQuery.ThenBy(keySelector)` or `orderedQuery.ThenByDescending(keySelector)` to order rows that have equal preceding keys. These methods require an ordered query and return an ordered query that can accept further tie-breakers.

```csharp
List<Product> products = Product.Where(item => item.Price > 50m)
    .OrderBy(item => item.Price)
    .ThenByDescending(item => item.Stock)
    .ThenBy(item => item.ID)
    .ToList();
```

This sorts by price ascending, then by stock descending, then by ID ascending. An ID tie-breaker makes ordering deterministic when the other keys are equal.

### Skip and Take

Use `query.Skip(count)` to skip rows and `query.Take(count)` to limit the result size. They return queries and do not read results until execution.

```csharp
List<Product> secondPage = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID).Skip(20).Take(20).ToList();
```

Order before paging. Skipping beyond the end produces an empty result; taking more rows than remain returns only the available rows. `Take(0)` produces an empty result. Use non-negative offsets and page sizes.

### ToList

Use `query.ToList()` to execute a query and materialize its results as `List<T>`. No matches produce an empty list.

```csharp
IQueryable<Product> query = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID);
List<Product> products = query.ToList();
```

Further LINQ operations on `products` run in application memory. Call query operators before `ToList()` when they should be part of Blueprint's query execution. Calling `query.ToList()` again executes the query again; the existing list is not automatically refreshed.

### Combined filtering and paging example

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

## Contains with case and accent options

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

### IgnoreCase: ignore uppercase and lowercase

```csharp
bool matchesCase = "Bodystocking".Contains("body", CompareOptions.IgnoreCase); // true
bool matchesGreekCase = "Φόρεμα".Contains("φόρεμα", CompareOptions.IgnoreCase); // true
bool matchesMissingAccent = "Φόρεμα".Contains("φορεμα", CompareOptions.IgnoreCase); // false

List<Product> products = Product.Where(item => item.Title.Contains("body", CompareOptions.IgnoreCase)).OrderBy(item => item.ID).Take(25).ToList();
```

`IgnoreCase` alone does not ignore accents. In particular, `StringComparison.OrdinalIgnoreCase` also does not ignore accents and is not a replacement for the combined options below.

### IgnoreNonSpace: ignore accents, preserve case

```csharp
bool matchesAccent = "Φόρεμα".Contains("Φορεμα", CompareOptions.IgnoreNonSpace); // true
bool matchesDifferentCase = "Φόρεμα".Contains("φορεμα", CompareOptions.IgnoreNonSpace); // Runtime-dependent; see below.

List<Product> products = Product.Where(item => item.Title.Contains("Φορεμα", CompareOptions.IgnoreNonSpace)).OrderBy(item => item.ID).Take(25).ToList();
```

`IgnoreNonSpace` ignores diacritics such as Greek tonos. It also ignores other nonspacing diacritics, including diaeresis; it is not limited to the Greek acute accent. SQL uses a case-sensitive, accent-insensitive collation for this option. In memory, .NET linguistic comparison can also equate some case variants: the direct call above and the equivalent RAM filter returned true in the verified Windows/.NET environment, while the SQL filter returned false. Do not assume identical SQL/RAM case behavior for this option alone. Combine it with `IgnoreCase` when both case and accents should be ignored.

### Combine the options

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

### Options, nulls, and SQL translation

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

## StartsWith with case and accent options

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

## Single rows and aggregates

### FirstOrDefault

The Entity API provides `Product.FirstOrDefault()` and `Product.FirstOrDefault(predicate)`. LINQ also provides `query.FirstOrDefault()` and `query.FirstOrDefault(predicate)` on a composed query.

```csharp
Product? first = Product.FirstOrDefault(item => item.Price > 50m);
Product? cheapest = Product.Where(item => item.Price > 50m).OrderBy(item => item.Price).ThenBy(item => item.ID).FirstOrDefault();
```

For entity queries, no matches return null. Multiple matches are allowed; only the first row is returned. Without ordering, the chosen row is not guaranteed. The parameterless Entity API form considers all rows. There is no direct language overload; start with `Product.Where(predicate, language)` when needed.

### SingleOrDefault

Use this when zero or one row is valid, and multiple matches should be treated as an error. The Entity API provides `Product.SingleOrDefault(long? language = null)` and `Product.SingleOrDefault(predicate, long? language = null)`. LINQ provides `query.SingleOrDefault()` and `query.SingleOrDefault(predicate)`.

```csharp
Product? product = Product.SingleOrDefault(item => item.ID == productID);
Product? translatedProduct = Product.SingleOrDefault(item => item.ID == productID, 2);
```

For entity queries, no matches return null; more than one match throws `InvalidOperationException`. `Product.SingleOrDefault()` checks all rows, so it throws if the table contains more than one row. Use a configured language ID for the optional language argument.

### Any

The Entity API provides `Product.Any()` and `Product.Any(predicate)`. LINQ provides `query.Any()` and `query.Any(predicate)` on a composed query. All return `bool`.

```csharp
bool hasProducts = Product.Any();
bool exists = Product.Any(item => item.SKU == "CAMERA-001");
bool hasAffordableStock = Product.Where(item => item.Price < 200m).Any(item => item.Stock > 0);
```

No matches return false; one or more matches return true. Use `Any` when only existence matters, without materializing a list or requesting a count. There is no direct language overload; use a language-aware `Where` first.

### Count and LongCount

These are LINQ terminal methods on a query, not static Entity API methods. Use `query.Count()` for an `int` result or `query.LongCount()` for a `long` result. Both also accept a predicate.

```csharp
IQueryable<Product> query = Product.Where(item => item.Price > 50m);
int count = query.Count();
long longCount = query.LongCount();
int inStockCount = query.Count(item => item.Stock > 0);
long inStockLongCount = query.LongCount(item => item.Stock > 0);
```

No matches return zero. Count before `Skip` and `Take` to obtain a total for pagination; counting a paged query counts only that page. Each terminal call executes separately, so a count and a subsequent page read can observe different data after concurrent writes.

### Combined result examples

```csharp
Product? product = Product.SingleOrDefault(item => item.ID == productID);
Product? first = Product.FirstOrDefault(item => item.Price > 50m);
bool exists = Product.Any(item => item.SKU == "CAMERA-001");
int count = Product.Where(item => item.Price > 50m).Count();
List<string> skus = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID).Select(item => item.SKU).ToList();
```

`SingleOrDefault` returns null for no rows and throws when more than one row matches. `FirstOrDefault` permits multiple matches, but without ordering the chosen row is not guaranteed. Use `Where(...).OrderBy(...).FirstOrDefault()` when order matters.

The parameterless `FirstOrDefault()`, `SingleOrDefault()`, and `Any()` forms operate on all rows. `Count` and `LongCount` are standard LINQ terminal operations on a query.

## Query a language

```csharp
Product? frenchProduct = Product.SingleOrDefault(item => item.ID == productID, 2);
List<Product> frenchProducts = Product.Where(item => item.Title == "Appareil photo", 2).ToList();
Product? firstFrenchProduct = Product.Where(item => item.Price > 50m, 2).OrderBy(item => item.ID).FirstOrDefault();
```

The language argument is a configured `long` ID. It affects multilingual filters, projections, and the language selected on returned entities. Without an argument, queries use the configured language with an empty prefix. `FirstOrDefault` and `Any` do not have direct language overloads; use the language-aware `Where` form before the terminal operation.

## Create

`Product.Create()` returns an unsaved instance of Blueprint's generated entity class. Use it for both DB and RAM contracts, including abstract contracts.

```csharp
Product product = Product.Create();
product.SKU = "CAMERA-DRAFT";
product.Title = "Camera";
product.Price = 120m;
```

No row is inserted, and assigning properties does not save anything. Leave `ID` at zero and call `Product.Insert(product)` when the object is ready to persist.

## Insert

Use `Product.Insert(entity)` or `Product.Insert(IEnumerable<Product> entities)`. Both return the inserted row count as an `int`.

```csharp
Product product = Product.Create();
product.SKU = "CAMERA-001";
product.Title = "Camera";
product.Price = 120m;
int insertedRows = Product.Insert(product);
```

`Create` does not write to the database. Leave `ID` at zero; insertion assigns the generated ID to the same instance. An empty collection returns zero. SQL constraints, such as a duplicate value in a unique index, can cause insertion to fail.

## Update

Use `Product.Update(entity)` or `Product.Update(IEnumerable<Product> entities)`. Both return the affected row count as an `int`; an empty collection returns zero.

```csharp
Product product = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product was not found.");
product.Price = 135m;
int updatedRows = Product.Update(product);
```

The update writes mapped values, including translations. It is not an automatic dirty-field patch. Load the row first to preserve other values. `Update(IEnumerable<Product>)` accepts a collection. A direct update can return zero when the row does not exist; a queued transaction update treats a missing row as a concurrency failure.

## Delete

`Product.Delete(predicate)` returns the deleted row count as an `int`, or zero when nothing matches.

```csharp
int deletedRows = Product.Delete(item => item.ID == productID);
```

Delete uses the supplied predicate and can affect multiple rows. The static method does not take an entity instance. For a transaction, use `transaction.AddDelete(loadedProduct)` instead.

## Group multiple writes

Separate direct CRUD calls are not one shared atomic transaction. Use [TransactionFactory](Database-Transactions.md) when inserts, updates, deletes, and business checks must all succeed together. There is no public consumer `SaveChanges()` step: direct CRUD saves explicitly, and `ExecuteTransactionAsync` handles transaction commit or rollback.

## Query limits and cached entities

SQL queries must be translatable by the provider. Arbitrary C# methods and `NotMapped` getters do not automatically become SQL. Materialize a suitably filtered set before applying application-only logic.

For `BlueprintEntityRAM` contracts, eligible queries execute against in-memory snapshots; others use SQL. In-memory string comparisons can differ from the database collation. Returned entities remain independent objects. See [RAM Caching](Database-RAMCaching.md) for query routing and paging behavior.

## Related topics

[EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md) · [Transactions](Database-Transactions.md) · [Multilingual Entities](Entitites-MultilingualEntities.md) · [RAM Caching](Database-RAMCaching.md)
