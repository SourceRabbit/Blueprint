# Data Annotations

Annotations on an entity class describe its SQL mapping, constraints, relationships, and optional behavior. Apply them to the contract before registering it. Registration reads the annotations and synchronizes supported schema changes.

## Annotation reference

Select an annotation to jump to its description, example, and usage notes. The behavior documented here applies to the current SQL-backed `BlueprintEntityDB` and `BlueprintEntityRAM` classes; RAM entities use the same SQL mapping and write constraints.

| Annotation | Namespace | Purpose |
| --- | --- | --- |
| [Entity](#entity) | `Blueprint.Entities.Annotations` | Marks a contract; optionally sets table name and `Schema` (default `dbo`). |
| [EntityIndex](#entityindex) | `Blueprint.Entities.Annotations` | Defines a named index over mapped properties; `IsUnique` defaults to false. |
| [Multilingual](#multilingual) | `Blueprint.Entities.Annotations` | Stores a separate value for each configured language. |
| [FallbackToDefaultLanguage](#fallbacktodefaultlanguage) | `Blueprint.Entities.Annotations` | Reads the default value when the selected translation is null or empty. |
| [AutoInclude](#autoinclude) | `Blueprint.Entities.Annotations` | Loads a navigation through the target entity's own DB or RAM executor. |
| [Required](#required) | `System.ComponentModel.DataAnnotations` | Requires a non-null stored value; applies to every multilingual column. |
| [MaxLength](#maxlength) | `System.ComponentModel.DataAnnotations` | Sets the maximum stored length. |
| [StringLength](#stringlength) | `System.ComponentModel.DataAnnotations` | Sets the maximum stored string length; minimum length needs application validation. |
| [Timestamp](#timestamp) | `System.ComponentModel.DataAnnotations` | Maps a SQL Server rowversion concurrency token. |
| [ConcurrencyCheck](#concurrencycheck) | `System.ComponentModel.DataAnnotations` | Includes a property in optimistic concurrency checks. |
| [Column](#column) | `System.ComponentModel.DataAnnotations.Schema` | Sets the physical column name and optionally SQL `TypeName`. |
| [NotMapped](#notmapped) | `System.ComponentModel.DataAnnotations.Schema` | Excludes a property from storage. |
| [ForeignKey](#foreignkey) | `System.ComponentModel.DataAnnotations.Schema` | Identifies the scalar key for a reference navigation. |
| [InverseProperty](#inverseproperty) | `System.ComponentModel.DataAnnotations.Schema` | Pairs a navigation with the corresponding navigation on the other entity. |
| [Precision](#precision) | `Microsoft.EntityFrameworkCore` | Sets decimal precision and scale. |
| [DefaultValue](#defaultvalue) | `System.ComponentModel` | Configures a SQL default for the column. |
| [ReadOnly](#readonly) | `System.ComponentModel` | Allows an initial value but rejects later changes when saving. |

Caching is selected by deriving the entity class from `BlueprintEntityRAM`; the former `[RAMCached]` annotation has been removed. `AutoInclude` never enables caching for a `BlueprintEntityDB` class. See [AutoInclude data sources](Database-RAMCaching.md#autoinclude-data-sources).

Blueprint does not run a general form-validation pipeline for every .NET validation attribute. Validate user input in your application; use [transaction validation](Database-Transactions.md#validate-before-commit) for checks that must succeed before database commit.

## Reading the examples

The examples below are independent. Property-only snippets belong inside a public abstract entity class marked with `[Entity]` and derived from `BlueprintEntityDB` or `BlueprintEntityRAM`. Full class examples define their own contracts; do not combine repeated `Product`, `Brand`, or `Category` declarations into one namespace. Register the contracts before using their Entity API, and create instances through `Create()`.

Use the namespaces needed by each example:

```csharp
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
```

## Blueprint annotations

### Entity

Apply `[Entity]` to each registered entity class. `[Entity("Categories", Schema = "catalog")]` sets an explicit table and schema:

```csharp
[Entity("Categories", Schema = "catalog")]
public abstract class Category : BlueprintEntityDB
{
    public abstract string Name { get; set; }
}
```

This contract maps to `catalog.Categories`. With `[Entity]` alone, the table name is the class name and the schema is `dbo`. Each registered class must declare its own annotation; it is not inherited from a base contract. The class must be public, non-sealed, and satisfy the [entity contract requirements](Entitites-BlueprintEntity.md#define-an-entity).

### EntityIndex

Apply `[EntityIndex(name, propertyNames, IsUnique = ...)]` to a class to define an index over mapped scalar properties. `IsUnique` defaults to false.

```csharp
[Entity("Products")]
[EntityIndex("UX_Products_SKU", nameof(SKU), IsUnique = true)]
public abstract class Product : BlueprintEntityDB
{
    [MaxLength(64)]
    public abstract string SKU { get; set; }
}
```

The example requires unique SKU values. Supply C# property names, such as `nameof(SKU)`, rather than physical column names. Pass several property names for a composite index, or apply the annotation more than once for separate indexes. Navigation and `NotMapped` properties cannot be indexed. Creating a unique index can fail if existing rows contain duplicates; see [Schema Synchronization](Database-SchemaSynchronization.md).

### Multilingual

Apply `[Multilingual]` to a mapped string property to store a separate value for every configured language:

```csharp
[Multilingual]
public abstract string? Title { get; set; }
```

`CurrentLanguage` selects the value read or assigned on an instance. Queries select a language through the Entity API. Abstract properties are supported; implemented multilingual properties require overridable accessors. Without fallback, a missing translation stays null or empty when read. See [Multilingual Entities](Entitites-MultilingualEntities.md) for language configuration and read/write examples.

### FallbackToDefaultLanguage

Combine `[FallbackToDefaultLanguage]` with `[Multilingual]` when a missing translation should display the default-language value:

```csharp
[Multilingual]
[FallbackToDefaultLanguage]
public abstract string? Title { get; set; }
```

Fallback applies when the selected translation is null or `""`; whitespace is returned unchanged. It affects reads, including SQL and RAM predicates and projections, without changing the stored translation or `CurrentLanguage`. The default language is the configured language with an empty prefix. Registration rejects this annotation without `Multilingual`. See [Default-language fallback](Entitites-MultilingualEntities.md#default-language-fallback).

### AutoInclude

Apply `[AutoInclude]` to a mapped reference or collection navigation to load it when complete entities are materialized:

```csharp
[Entity("Brands")]
public abstract class Brand : BlueprintEntityRAM
{
    public abstract string Name { get; set; }
}

[Entity("Products")]
public abstract class Product : BlueprintEntityDB
{
    public abstract long? BrandID { get; set; }

    [ForeignKey(nameof(BrandID)), AutoInclude]
    public abstract Brand? Brand { get; set; }
}
```

In this example, reading a product reads its root from SQL and its brand through the RAM executor. Each related entity chooses its own executor, including nested includes; transactions read the entire graph from SQL. `AutoInclude` requests loading, not cascading writes or caching of DB entities. Include traversal stops when a type repeats on the same path. See [Relationships](Entitites-Relationships.md#load-related-data) and [Query Executors](Database-QueryExecutors.md).

## .NET and EF Core annotations

### Required

Use `[Required]` to make the mapped column non-nullable, even if the C# property is declared nullable:

```csharp
[Required]
public abstract string? Code { get; set; }
```

For a multilingual property, this applies to every language column. Blueprint does not automatically run the attribute's form-validation logic; validate empty or whitespace-only user input in your application. See [Nullability and defaults](#nullability-and-defaults).

### MaxLength

Use `[MaxLength(length)]` to set the maximum stored length of a string property:

```csharp
[MaxLength(64)]
public abstract string? SKU { get; set; }
```

This configures the column's maximum length; it does not truncate assigned values or validate a form. For multilingual strings, the limit applies to each generated language column. Validate input length before saving when the application needs a friendly error.

### StringLength

Use `[StringLength(maximumLength)]` as another way to configure the maximum stored string length:

```csharp
[StringLength(64, MinimumLength = 3)]
public abstract string? Code { get; set; }
```

Blueprint uses `MaximumLength` for mapping. `MinimumLength = 3` does not create a database constraint and is not automatically checked by CRUD calls; enforce it in application validation. If both `MaxLength` and `StringLength` are present, Blueprint takes the length from `MaxLength`.

### Timestamp

Apply `[Timestamp]` to a `byte[]` property to use SQL Server's automatically generated `rowversion` as a concurrency token:

```csharp
[Timestamp]
public abstract byte[] Version { get; set; }
```

SQL Server generates the token on insert and changes it when the row is updated. It is not a date or time, and it is not inherited from `BlueprintEntity`. Load the entity before editing, keep its token unchanged, and save through the Entity API. An outdated token can cause `DbUpdateConcurrencyException`. See [Concurrency](#concurrency) for update and delete behavior.

### ConcurrencyCheck

Apply `[ConcurrencyCheck]` to an existing stored property to include its value in optimistic concurrency checks:

```csharp
[ConcurrencyCheck]
public abstract int Revision { get; set; }
```

Blueprint uses the value supplied on the entity as the expected stored value. Load the entity first and leave this value unchanged while editing other fields. This annotation neither generates nor increments `Revision`, and Blueprint does not retain a separate original-value copy for the consumer. Use `Timestamp` when SQL Server should generate a token that changes on every update. See [Concurrency](#concurrency).

### Column

Use `[Column(name)]` to set a physical column name, and optionally `TypeName` to specify a SQL type:

```csharp
[Column("DisplayName", TypeName = "nvarchar(200)")]
public abstract string? Name { get; set; }
```

The consumer still uses `Name`; SQL stores the value in `DisplayName`. For multilingual properties, the configured language suffix is appended to the chosen column name. `TypeName` is provider-specific SQL configuration. Changing a column name does not instruct schema synchronization to rename or migrate existing data.

### NotMapped

Apply `[NotMapped]` to a property that should not be stored, such as a computed display value:

```csharp
public abstract string Code { get; set; }
public abstract string Name { get; set; }

[NotMapped]
public string DisplayLabel
{
    get
    {
        return Code + " - " + Name;
    }
}
```

The example stores `Code` and `Name` but creates no column for `DisplayLabel`. An implemented `NotMapped` getter can omit a setter. Evaluate such getters after materializing results; neither SQL translation nor RAM query eligibility is guaranteed for an unmapped property. See [Query limits](Entitites-BlueprintEntity.md#query-limits-and-cached-entities).

### ForeignKey

In Blueprint, place `[ForeignKey(nameof(keyProperty))]` on a reference navigation to identify its scalar key property:

```csharp
[Entity("Brands")]
public abstract class Brand : BlueprintEntityDB
{
    public abstract string Name { get; set; }
}

[Entity("Products")]
public abstract class Product : BlueprintEntityDB
{
    public abstract long? ManufacturerID { get; set; }

    [ForeignKey(nameof(ManufacturerID))]
    public abstract Brand? Brand { get; set; }
}
```

Here `ManufacturerID` points to `Brand.ID`. Without the annotation, Blueprint expects the navigation name followed by `ID`, which would be `BrandID`. A nullable key represents an optional relationship; use a non-nullable `long` for a required one. The annotation defines the relationship; add `AutoInclude` when it should load automatically. See [Relationships](Entitites-Relationships.md).

### InverseProperty

Use `[InverseProperty(nameof(otherNavigation))]` to identify the corresponding navigation on the related entity:

```csharp
[Entity("Brands")]
public abstract class Brand : BlueprintEntityDB
{
    [InverseProperty(nameof(Product.Brand))]
    public abstract ICollection<Product> Products { get; set; }
}

[Entity("Products")]
public abstract class Product : BlueprintEntityDB
{
    public abstract long? BrandID { get; set; }

    [ForeignKey(nameof(BrandID))]
    public abstract Brand? Brand { get; set; }
}
```

The example pairs `Brand.Products` with `Product.Brand`; it does not create an additional foreign-key column. Explicit pairing is useful when multiple relationships involve the same types. The inverse collection is not automatically loaded unless it also has `AutoInclude`.

### Precision

Use `[Precision(precision, scale)]` to configure a decimal property's total number of digits and digits after the decimal point:

```csharp
[Precision(18, 2)]
public abstract decimal Price { get; set; }
```

This maps `Price` with precision 18 and scale 2. Blueprint uses scale zero if only precision is supplied. The annotation configures storage; apply any business rounding or input-validation rules in your application.

### DefaultValue

Use `[DefaultValue(value)]` to configure a database default for a column:

```csharp
[DefaultValue(10)]
public abstract int SortOrder { get; set; }
```

This declares a SQL default of 10. It does not initialize `SortOrder` to 10 when `Create()` returns an object; the database default applies when an insert uses the generated default. Assign values explicitly when needed before saving. A default can also support adding a required column to an existing table; see [Schema Synchronization](Database-SchemaSynchronization.md#supported-changes).

### ReadOnly

Use `[ReadOnly(true)]` to permit an initial stored value but reject subsequent changes when saving through Blueprint:

```csharp
[ReadOnly(true)]
public abstract string Code { get; set; }
```

Assign `Code` before insertion. The C# setter remains accessible, but changing a persisted value is rejected on save. This also applies to individual multilingual columns. The annotation does not create a SQL constraint that prevents external applications from changing the value. See [Read-only and computed properties](#read-only-and-computed-properties).

## Example contract

This separate contract demonstrates common annotations. Register it with `database.RegisterEntities(typeof(Category))` before using `Category.Create()` or querying it.

```csharp
using Blueprint.Entities.Annotations;
using Blueprint.Entities;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueprintManual
{
    /// <summary>Describes a catalog category with an immutable code and concurrency token.</summary>
    [Entity("Categories", Schema = "catalog")]
    [EntityIndex("UX_Categories_Code", nameof(Code), IsUnique = true)]
    public abstract class Category : BlueprintEntityDB
    {
        [Required, MaxLength(40), ReadOnly(true)]
        public abstract string Code { get; set; }

        [Column("DisplayName"), MaxLength(200)]
        public abstract string Name { get; set; }

        [DefaultValue(0)]
        public abstract int SortOrder { get; set; }

        [Timestamp]
        public abstract byte[] Version { get; set; }

        [NotMapped]
        public string DisplayLabel
        {
            get
            {
                return Code + " - " + Name;
            }
        }
    }
}
```

The index takes C# property names, such as `nameof(Code)`, rather than SQL column names. The column named `DisplayName` continues to be exposed as `Name` in C#.

## Nullability and defaults

For ordinary reference properties, nullable reference annotations affect whether the column permits `NULL`. An explicit `[Required]` also makes a property required. Nullable value types such as `decimal?` represent optional values.

For multilingual properties, use an explicit `[Required]` only when **every configured translation** must exist. A non-nullable C# reference annotation alone does not require all translations. Length, precision, and column options apply to the generated language columns.

Use `[FallbackToDefaultLanguage]` on a mapped `[Multilingual]` string to opt into default-language fallback for `null` and `""`. Whitespace remains unchanged. Fallback affects reads in SQL and RAM cache; writes still target the selected translation and `[Required]` continues to constrain stored values. See [Default-language fallback](Entitites-MultilingualEntities.md#default-language-fallback) for examples and query behavior.

`DefaultValue` defines a database default; it is not a general initializer for an object returned by `Create()`. Assign values the application needs explicitly. See [Schema Synchronization](Database-SchemaSynchronization.md) for adding required fields to populated tables.

## Read-only and computed properties

`ReadOnly(true)` leaves the setter available for initialization. Later changes are rejected on save, including changes to individual translations. This is a persistence rule and does not add a SQL constraint that prevents another application from editing the column.

`NotMapped` can expose a computed class getter, as above. Evaluate it after loading the entity. A query using that property may not translate to SQL; a C# getter is not automatically a SQL expression.

## Concurrency

Load the existing entity before editing it so that its concurrency token is available. `Timestamp` uses a generated `byte[]` rowversion. `ConcurrencyCheck` uses the supplied value of the annotated property as the expected stored value. Keep that token unchanged while editing other fields: Blueprint does not retain a separate change-tracker copy of the value originally read. If another writer changes the protected row, saving can throw `DbUpdateConcurrencyException`.

Queued updates and deletes in a `DatabaseTransaction` check affected rows and concurrency tokens. A missing target row also fails the transaction. Handle the conflict by reloading and deciding whether to retry with a **new** transaction; do not silently overwrite the other writer's changes.

A static predicate delete such as `Product.Delete(item => item.ID == productID)` expresses a SQL filter. It does not carry a previously loaded entity's rowversion; use a queued entity delete when that concurrency check is required.

## Related topics

[EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md) · [Relationships](Entitites-Relationships.md) · [Schema Synchronization](Database-SchemaSynchronization.md) · [Transactions](Database-Transactions.md)
