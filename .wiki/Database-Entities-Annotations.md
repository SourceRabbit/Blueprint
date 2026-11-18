# Data Annotations

Annotations on an entity class describe its SQL mapping, constraints, relationships, and optional behavior. Apply them to the contract before registering it. Registration reads the annotations and synchronizes supported schema changes.

## Annotation reference

| Annotation | Namespace | Purpose |
| --- | --- | --- |
| `Entity` | `Blueprint.Entities.Annotations` | Marks a contract; optionally sets table name and `Schema` (default `dbo`). |
| `EntityIndex` | `Blueprint.Entities.Annotations` | Defines a named index over mapped properties; `IsUnique` defaults to false. |
| `Multilingual` | `Blueprint.Entities.Annotations` | Stores a separate value for each configured language. |
| `FallbackToDefaultLanguage` | `Blueprint.Entities.Annotations` | Requires `Multilingual`; reads the default value when the selected translation is null or empty, without changing stored translations. |
| `AutoInclude` | `Blueprint.Entities.Annotations` | Automatically loads the navigation through the target entity's own DB or RAM executor, independently of the parent's source. |
| `Required` | `System.ComponentModel.DataAnnotations` | Requires a stored value. For multilingual fields, applies to every language. |
| `MaxLength`, `StringLength` | `System.ComponentModel.DataAnnotations` | Sets the maximum stored length. `StringLength.MinimumLength` is not a database validation rule. |
| `Timestamp` | `System.ComponentModel.DataAnnotations` | Maps a SQL Server rowversion concurrency token. |
| `ConcurrencyCheck` | `System.ComponentModel.DataAnnotations` | Includes a property in optimistic concurrency checks. |
| `Column` | `System.ComponentModel.DataAnnotations.Schema` | Sets the physical column name and optionally SQL `TypeName`. |
| `NotMapped` | `System.ComponentModel.DataAnnotations.Schema` | Excludes a property from storage. |
| `ForeignKey`, `InverseProperty` | `System.ComponentModel.DataAnnotations.Schema` | Identifies reference keys and inverse navigation properties. |
| `Precision` | `Microsoft.EntityFrameworkCore` | Sets decimal precision and scale. |
| `DefaultValue` | `System.ComponentModel` | Configures a SQL default for the column. |
| `ReadOnly(true)` | `System.ComponentModel` | Allows an initial value but rejects later changes when saving. |

Caching is selected by deriving the entity class from `BlueprintEntityRAM`; the former `[RAMCached]` annotation has been removed. `AutoInclude` never enables caching for a `BlueprintEntityDB` class. See [AutoInclude data sources](Database-RAM-Caching.md#autoinclude-data-sources).

Blueprint does not run a general form-validation pipeline for every .NET validation attribute. Validate user input in your application; use [transaction validation](Database-Transactions.md#validate-before-commit) for checks that must succeed before database commit.

## Example contract

This separate contract demonstrates common annotations. Register it with `database.RegisterEntity<Category>()` before using `Category.Create()` or querying it.

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

Use `[FallbackToDefaultLanguage]` on a mapped `[Multilingual]` string to opt into default-language fallback for `null` and `""`. Whitespace remains unchanged. Fallback affects reads in SQL and RAM cache; writes still target the selected translation and `[Required]` continues to constrain stored values. See [Default-language fallback](Database-Multilingual-Entities.md#default-language-fallback) for examples and query behavior.

`DefaultValue` defines a database default; it is not a general initializer for an object returned by `Create()`. Assign values the application needs explicitly. See [Schema Synchronization](Database-Entities-Schema-Synchronization.md) for adding required fields to populated tables.

## Read-only and computed properties

`ReadOnly(true)` leaves the setter available for initialization. Later changes are rejected on save, including changes to individual translations. This is a persistence rule and does not add a SQL constraint that prevents another application from editing the column.

`NotMapped` can expose a computed class getter, as above. Evaluate it after loading the entity. A query using that property may not translate to SQL; a C# getter is not automatically a SQL expression.

## Concurrency

Load the existing entity before editing it so that its concurrency token is available. `Timestamp` uses a generated `byte[]` rowversion. `ConcurrencyCheck` uses the supplied value of the annotated property as the expected stored value. Keep that token unchanged while editing other fields: Blueprint does not retain a separate change-tracker copy of the value originally read. If another writer changes the protected row, saving can throw `DbUpdateConcurrencyException`.

Queued updates and deletes in a `DatabaseTransaction` check affected rows and concurrency tokens. A missing target row also fails the transaction. Handle the conflict by reloading and deciding whether to retry with a **new** transaction; do not silently overwrite the other writer's changes.

A static predicate delete such as `Product.Delete(item => item.ID == productID)` expresses a SQL filter. It does not carry a previously loaded entity's rowversion; use a queued entity delete when that concurrency check is required.

## Related topics

[EShop - Checkout Tutorial](Database-Tutorial-EShop-Checkout.md) · [Relationships](Data-Entities-Relationships.md) · [Schema Synchronization](Database-Entities-Schema-Synchronization.md) · [Transactions](Database-Transactions.md)
