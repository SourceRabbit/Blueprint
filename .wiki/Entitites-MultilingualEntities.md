# Multilingual Entities

Mark a property with `[Multilingual]` to store a value for each configured language. All translations belong to one row with one entity ID; languages do not create duplicate entity rows.

## Configure languages

Pass the language list to the `SqlDatabase` constructor. This example uses English as the default and French as the second language:

```csharp
using Blueprint.Fundamentals;
using System.Collections.Generic;

List<BlueprintLanguage> languages = new List<BlueprintLanguage>
{
    new BlueprintLanguage(1, string.Empty),
    new BlueprintLanguage(2, "FR")
};
```

Create each language with `BlueprintLanguage(long id, string prefix)`. Its `ID` and `Prefix` properties are read-only; supply their values through the constructor rather than an object initializer.

IDs are application-defined `long` values. Each ID and prefix must be unique; prefix comparison is case-insensitive. Exactly one language must have an empty prefix. Null or whitespace-only prefixes are invalid. The list must not be null or empty.

The language with the empty prefix is the default. Blueprint does not infer a locale from ID 1 or from a prefix: language labels and locale selection belong to your application.

## Declare translated fields

The [Product contract](Database-EShopCheckoutTutorial.md#define-a-contract) declares:

```csharp
[Multilingual, MaxLength(200)]
public abstract string? Title { get; set; }
```

This declaration belongs inside an abstract entity class derived from `BlueprintEntityDB` or `BlueprintEntityRAM`, with `Blueprint.Entities.Annotations` and `System.ComponentModel.DataAnnotations` imported. An implemented multilingual property must use overridable `virtual` accessors so the generated subclass can select its translation.

| Language ID | Prefix | SQL column for `Title` |
| --- | --- | --- |
| 1 | empty | `Title` |
| 2 | `FR` | `TitleFR` |

Generated suffixes use the uppercased prefix while preserving the base column name. `[Column("DisplayTitle")]` would produce `DisplayTitle` and `DisplayTitleFR` instead. Length and other mapped field settings apply to every language column.

## Write translations

```csharp
using Blueprint.Entities;

Product product = Product.Create();
product.SKU = "CAMERA-TRANSLATED-001";
product.Title = "Camera";
product.CurrentLanguage = 2;
product.Title = "Appareil photo";
product.Price = 120m;
Product.Insert(product);
```

The initial `CurrentLanguage` is the default language ID. Assigning a multilingual property changes only the selected translation. Ordinary properties, such as `Price`, are shared by all languages. Changing `CurrentLanguage` does not write to the database.

For an existing product, load it, select the language, edit its title, and explicitly update it:

```csharp
Product product = Product.SingleOrDefault(item => item.ID == productID, 2) ?? throw new InvalidOperationException("Product was not found.");
product.Title = "Nouvel appareil photo";
Product.Update(product);
```

An update includes the object's mapped translations, not just its current language. Loading before editing preserves the other translations. Use a queued update when this change belongs to a larger [transaction](Database-Transactions.md).

## Read a language

```csharp
List<Product> products = Product.Where(item => item.Title == "Appareil photo", 2).ToList();
Product? product = Product.SingleOrDefault(item => item.ID == productID, 2);
```

The language argument selects the translation for query predicates, projections, and materialized entities, including related entities. Omitting it uses the default language. An unknown language ID is rejected.

Without `[FallbackToDefaultLanguage]`, reads return the selected translation as stored, including `null` or an empty string. Add the annotation to individual multilingual properties when a missing translation should use the default language.

## Default-language fallback

Apply `[FallbackToDefaultLanguage]` alongside `[Multilingual]` before registering the contract:

```csharp
[Multilingual]
[FallbackToDefaultLanguage]
public abstract string? Title { get; set; }
```

Both annotations belong to `Blueprint.Entities.Annotations`. Registration rejects `[FallbackToDefaultLanguage]` without `[Multilingual]`, or on a property that is not a mapped string.

When the selected translation is `null` or `""`, reads return the default-language value. The default is the language with an empty `Prefix`, regardless of its ID or position in the language list. Whitespace-only values, such as `" "`, are returned unchanged. If the default value is also missing, its exact `null` or empty value is returned; no other language is searched. Properties without the annotation keep their existing behavior.

With the annotated Title and the language configuration above:

```csharp
Product product = Product.Create();
product.Title = "Camera";
product.CurrentLanguage = 2;
product.Title = null;
Console.WriteLine(product.Title); // Camera
Console.WriteLine(product.CurrentLanguage); // 2
```

| Selected translation | Default value | Returned value |
| --- | --- | --- |
| `"Appareil photo"` | `"Camera"` | `"Appareil photo"` |
| `null` | `"Camera"` | `"Camera"` |
| `""` | `"Camera"` | `"Camera"` |
| `" "` | `"Camera"` | `" "` |
| `""` | `null` | `null` |
| `null` | `""` | `""` |

**Fallback affects reads only.** It does not change `CurrentLanguage`, copy text into the selected translation, or update the database. Insert and update operations persist the raw translations, even when the getter displays the default text. Assigning a new `Title` still changes only the selected language. Reading a fallback and then saving an unrelated property does not fill in the missing translation.

The same resolution rules apply to materialized entities, related entities, SQL predicates and projections, and RAM-cached queries, including `Count`, ordering, and paging. For example, the following query can match a product whose French title is missing and whose default title is `"Camera"`:

```csharp
List<string?> titles = Product.Where(item => item.Title == "Camera", 2).Select(item => item.Title).ToList();
```

SQL and RAM use the same fallback rules; ordinary comparison and sorting semantics still follow their respective providers, as described in [RAM Caching](Database-RAMCaching.md#queries-and-sql-fallback). Queries inside a transaction continue to read SQL directly. Unknown language IDs remain invalid. Adding or removing this annotation changes read behavior without adding columns or modifying existing translations.

## Required translations and configuration changes

An explicit `[Required]` requires a value for every configured translation. Non-nullable C# reference syntax alone does not require every multilingual column. Use nullable strings for optional translations and validate requirements appropriate to your application.

`[FallbackToDefaultLanguage]` does not satisfy or relax `[Required]`; a missing stored translation is still missing even when a read displays default-language text.

The database copies and validates its language configuration during construction. Mutating the original list has no effect. Read the configuration through `database.Languages`. To change languages, stop background work that uses the database, dispose the current database, create one with the new configuration, and register its contracts. Re-query entities from that model.

Adding a language may add SQL columns during registration. A new required translation on populated data needs an explicit default or migration. Removing a language does not automatically delete its old columns. See [Schema Synchronization](Database-SchemaSynchronization.md).

## Related topics

[Getting Started](GettingStarted.md) · [Data Annotations](Entitites-DataAnnotations.md) · [Queries and CRUD](Entitites-BlueprintEntity.md#queries-and-crud) · [Schema Synchronization](Database-SchemaSynchronization.md)
