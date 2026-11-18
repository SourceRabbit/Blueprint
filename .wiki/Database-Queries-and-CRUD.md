# Queries and CRUD

Use the static Entity API on registered contracts for reads and writes. Import `Blueprint.Entities` and `System.Linq`; initialize the database and register the contracts as shown in the [complete checkout example](Database-Tutorial-EShop-Checkout.md#complete-example-in-main).

The Entity API keeps the same signatures for reads and writes. The former `Collection<TEntity>` type and `database.Collection<TEntity>()` helpers have been removed. Use the Entity API for these operations, and import `Blueprint.Database.Queries` for `TextSearch` extensions.

Examples use the [Product contract](Database-Tutorial-EShop-Checkout.md#define-a-contract). `productID` represents an existing product ID supplied by your application. Run each write example independently and use unique SKUs when inserting again.

Both DB and RAM entity classes use this same LINQ syntax. Blueprint selects `QueryExecutor_Database` for `BlueprintEntityDB` and `QueryExecutor_RAM` for `BlueprintEntityRAM`. Every `AutoInclude` target selects its own executor independently of its parent, so results can contain both SQL and RAM entities. See [Query Executors](Database-Query-Executors.md) for a release-year/price example and mixed-source behavior.

## Filter, order, and page

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
bool matchesDifferentCase = "Φόρεμα".Contains("φορεμα", CompareOptions.IgnoreNonSpace); // false

List<Product> products = Product.Where(item => item.Title.Contains("Φορεμα", CompareOptions.IgnoreNonSpace)).OrderBy(item => item.ID).Take(25).ToList();
```

`IgnoreNonSpace` ignores diacritics such as Greek tonos. It also ignores other nonspacing diacritics, including diaeresis; it is not limited to the Greek acute accent. It does not enable case-insensitive matching by itself.

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

| Options | Case | Accents | SQL Server collation |
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
- Ordinary Greek and English title searches follow the requested case/accent rules in both paths. .NET linguistic comparisons and SQL Server collations are separate implementations and can differ for specialized Unicode equivalences or sorting-version changes. This is a linguistic search API, not an ordinal identity comparison.
- Explicit collation and arbitrary substring searches do not guarantee index seeks. This feature preserves the existing query routing and cache filtering before result copies; it does not introduce a full-text index.

See [RAM Caching](Database-RAM-Caching.md#queries-and-sql-fallback), [.NET CompareOptions](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.compareoptions), and [EF Core collations](https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity).

## StartsWith with case and accent options

`TextSearch.StartsWith(this string? source, string? value, CompareOptions options)` checks only the beginning of the text. Import the same `Blueprint.Database.Queries` and `System.Globalization` namespaces used for `Contains`.

```csharp
bool ignoresCase = "Bodystocking".StartsWith("body", CompareOptions.IgnoreCase); // true
bool keepsAccent = "Φόρεμα".StartsWith("φορεμα", CompareOptions.IgnoreCase); // false
bool ignoresAccent = "Φόρεμα".StartsWith("Φορεμα", CompareOptions.IgnoreNonSpace); // true
bool keepsCase = "Φόρεμα".StartsWith("φορεμα", CompareOptions.IgnoreNonSpace); // false

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

## Insert

```csharp
Product product = Product.Create();
product.SKU = "CAMERA-001";
product.Title = "Camera";
product.Price = 120m;
int insertedRows = Product.Insert(product);
```

`Create` does not write to the database. Leave `ID` at zero; insertion assigns the generated ID to the same instance. `Insert(IEnumerable<Product>)` is available for a collection.

## Update

```csharp
Product product = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product was not found.");
product.Price = 135m;
int updatedRows = Product.Update(product);
```

The update writes mapped values, including translations. It is not an automatic dirty-field patch. Load the row first to preserve other values. `Update(IEnumerable<Product>)` accepts a collection. A direct update can return zero when the row does not exist; a queued transaction update treats a missing row as a concurrency failure.

## Delete

```csharp
int deletedRows = Product.Delete(item => item.ID == productID);
```

Delete uses the supplied predicate and can affect multiple rows. The static method does not take an entity instance. For a transaction, use `transaction.AddDelete(loadedProduct)` instead.

## Group multiple writes

Separate direct CRUD calls are not one shared atomic transaction. Use [TransactionFactory](Database-Transactions.md) when inserts, updates, deletes, and business checks must all succeed together. There is no public consumer `SaveChanges()` step: direct CRUD saves explicitly, and `ExecuteTransactionAsync` handles transaction commit or rollback.

## Query limits and cached entities

SQL queries must be translatable by the provider. Arbitrary C# methods and `NotMapped` getters do not automatically become SQL. Materialize a suitably filtered set before applying application-only logic.

For `BlueprintEntityRAM` contracts, eligible queries execute against in-memory snapshots; others use SQL. In-memory string comparisons can differ from the database collation. Returned entities remain independent objects. See [RAM Caching](Database-RAM-Caching.md) for query routing and paging behavior.

## Related topics

[EShop - Checkout Tutorial](Database-Tutorial-EShop-Checkout.md) · [Transactions](Database-Transactions.md) · [Multilingual Entities](Database-Multilingual-Entities.md) · [RAM Caching](Database-RAM-Caching.md)
