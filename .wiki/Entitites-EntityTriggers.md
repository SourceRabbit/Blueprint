# Entity Triggers

Declare a trigger in C# when a stored value must stay in sync with other rows or columns. SQL Server applies the rule in the same transaction as the write, including writes from another SQL client. A rollback also rolls back the change made by the trigger.

## Contents

- [Declare a trigger](#declare-a-trigger)
- [MaintainCount](#maintaincount)
- [MaintainHierarchyLevel](#maintainhierarchylevel)
- [MaintainValue](#maintainvalue)
- [MaintainDistinctRows](#maintaindistinctrows)
- [Registration and existing rows](#registration-and-existing-rows)
- [Related topics](#related-topics)

## Declare a trigger

Place a public static method marked `[EntityTriggers]` on the entity that declares the rule. The method returns `void` and takes one `EntityTriggerBuilder<TEntity>` parameter, where `TEntity` is the declaring entity. Blueprint calls the method during registration. Use a trigger name that is unique within its SQL schema. Each stored property can be maintained by one rule.

The examples below are independent models. Both place the trigger declarations in a `Triggers` region after the `Properties` region and use these imports.

```csharp
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Entities.Triggers;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
```

## MaintainCount

Use `MaintainCount<TTarget>` on the entity whose rows you want to count. Here each product with a `BrandID` contributes one to that brand’s `ProductsCount`. A product with a null `BrandID` contributes zero. Moving a product to another brand decrements the old brand and increments the new one. Deleting the product removes its contribution.

```csharp
[Entity("Brands")]
public abstract class Brand : BlueprintEntityDB
{
    #region Properties

    [DefaultValue(0), DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public abstract int ProductsCount { get; }

    #endregion
}

[Entity("Products")]
public abstract class Product : BlueprintEntityDB
{
    #region Properties

    public abstract long? BrandID { get; set; }

    [ForeignKey(nameof(BrandID))]
    public abstract Brand? Brand { get; set; }

    public abstract bool IsActive { get; set; }

    #endregion

    #region Triggers

    /// <summary>Keeps each brand’s product count in sync with its assigned products.</summary>
    /// <param name="triggers">The trigger declarations for Product.</param>
    [EntityTriggers]
    public static void ConfigureTriggers(EntityTriggerBuilder<Product> triggers)
    {
        triggers.MaintainCount<Brand>(
            name: "TR_Products_BrandProductsCount",
            foreignKey: product => product.BrandID,
            counter: brand => brand.ProductsCount);
    }

    #endregion
}
```

The foreign key must be a mapped `long` or `long?` referencing the target ID. The counter is a stored `int`. The attributes on `ProductsCount` keep the zero default and database-generated mapping even when the application registers `Brand` on its own.

Omitting `include` counts every assigned product, including inactive products. To count only active products, add `include: product => product.IsActive` to the call. The predicate supports comparisons of mapped, non-nullable Boolean, enum and integer properties with literals, combined with `&&`, `||` and `!`.

## MaintainHierarchyLevel

Use `MaintainHierarchyLevel` for a tree with a nullable `long` foreign key to the same entity and a stored `int` level. Expose the level as a getter with `[DefaultValue(0), DatabaseGenerated(DatabaseGeneratedOption.Computed)]`. Declare the rule in the entity's `[EntityTriggers]` method:

```csharp
triggers.MaintainHierarchyLevel("TR_Categories_Level", category => category.ParentID, category => category.Level);
```

A root has level zero. Inserting or changing a parent updates the depth of the affected subtree in the same transaction. Multi-row operations use the final parent relationships, including moves that affect overlapping subtrees. Cycles are rejected. Registration initializes levels when installing or repairing the trigger. Ordinary changes to other columns do not traverse the tree.

## MaintainValue

Use `MaintainValue` on the entity that stores the result. The two rules below form a chain. The VAT category derives its multiplier from the rate. The product uses its own net price and the multiplier of its assigned category.

```csharp
[Entity("ProductVatCategories")]
public abstract class ProductVatCategory : BlueprintEntityDB
{
    #region Properties

    [Precision(5, 2)]
    public abstract decimal Rate { get; set; }

    [Precision(5, 4), DefaultValue(typeof(decimal), "1")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public abstract decimal PriceMultiplier { get; }

    #endregion

    #region Triggers

    /// <summary>Converts the VAT percentage into a price multiplier.</summary>
    /// <param name="triggers">The trigger declarations for ProductVatCategory.</param>
    [EntityTriggers]
    public static void ConfigureTriggers(EntityTriggerBuilder<ProductVatCategory> triggers)
    {
        triggers.MaintainValue(
            name: "TR_ProductVatCategories_PriceMultiplier",
            target: category => category.PriceMultiplier,
            value: category => 1m + category.Rate / 100m);
    }

    #endregion
}

[Entity("Products")]
public abstract class Product : BlueprintEntityDB
{
    #region Properties

    [Precision(18, 2)]
    public abstract decimal? NetPrice { get; set; }

    public abstract long VatCategoryID { get; set; }

    [ForeignKey(nameof(VatCategoryID))]
    public abstract ProductVatCategory VatCategory { get; set; }

    [Precision(18, 2), DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public abstract decimal? PriceWithVAT { get; }

    #endregion

    #region Triggers

    /// <summary>Keeps the stored price in sync with the net price and VAT category.</summary>
    /// <param name="triggers">The trigger declarations for Product.</param>
    [EntityTriggers]
    public static void ConfigureTriggers(EntityTriggerBuilder<Product> triggers)
    {
        triggers.MaintainValue(
            name: "TR_Products_PriceWithVAT",
            target: product => product.PriceWithVAT,
            value: product => product.NetPrice * product.VatCategory.PriceMultiplier);
    }

    #endregion
}
```

With a net price of `100.00` and a rate of `24.00`, the stored price is `124.00`. Updating just the category’s `Rate` to `13.00` changes its `PriceMultiplier` to `1.1300` and the product’s `PriceWithVAT` to `113.00`. The same update reaches every product in that category. Changing a single product’s net price or assigning it a different VAT category recalculates that product’s price. SQL Server’s `nested triggers` option must be enabled for chained rules.

The formula supports decimal literals, stored `decimal` or `decimal?` properties on the entity or a direct reference, and the operators `+`, `-`, `*` and `/`. SQL Server rounds the result to the destination column’s scale. A null net price produces a null result and a zero net price produces `0.00`. Give a required generated column a database default for the initial insert, as shown on `PriceMultiplier`. Formula dependencies must be acyclic.

## MaintainDistinctRows

Use `MaintainDistinctRows` when other tables determine which rows should exist. For example, a catalog can store one row for each category and attribute used by a listed product. The following declaration assumes that each `Listing` supplies a category, an attribute and an optional text value.

```csharp
// On CategoryFilter, with a unique index on (CategoryID, AttributeID).
[EntityTriggers]
public static void ConfigureTriggers(EntityTriggerBuilder<CategoryFilter> triggers)
{
    triggers.MaintainDistinctRows(
        name: "Sync_CategoryFilters",
        key: filter => new { filter.CategoryID, filter.AttributeID },
        rows: db => db.Rows<Listing>()
            .Where(item => item.IsListed)
            .Select(item => new
            {
                item.CategoryID,
                item.AttributeID,
                Value = item.Text == null ? null : item.Text.Trim()
            }))
        .WithValues<CategoryFilterValue>(v => v.CategoryFilterID, v => v.Value, row => row.Value)
        .OnChange<Listing>("TR_Listings_CategoryFilters",
            (_, changed) => changed.Select(item => new { item.CategoryID, item.AttributeID }),
            item => new { item.CategoryID, item.AttributeID, item.IsListed, item.Text });
}
```

`rows` describes the entire desired result using a composable, parameter-free LINQ projection that SQL Server can execute. Use a named projection such as `new { row.CategoryID }` even for a single key. The projected key names and types must match the target properties. Declare a unique index on those required properties in the same order. Blueprint inserts new keys, keeps the IDs of existing keys, and removes a key when its last source disappears. The rule owns the contents of its target tables.

`WithValues` adds an optional child table with a required foreign key to the parent and a required string column. Declare a unique index on that foreign key followed by the value. Null or empty projected values produce no child. A parent still exists if the query emits its key. Source queries can explicitly trim input, as in the example.

Declare an `OnChange` for **each source table** whose writes can change the result. Its `changed` query contains both the old and new rows from the whole SQL statement. Project both the old and new keys when a row moves. Keep eligibility conditions such as `IsListed` in `rows` so that an item leaving the result also refreshes its old key. Use `db.Rows<T>()` to follow related tables where necessary. The optional final property selector skips updates when all watched properties are unchanged.

Register the source, target and optional child contracts together. Schema synchronization generates one procedure per rule and one trigger per declared change source. Installing or repairing them reconciles existing rows in the schema transaction. Subsequent writes refresh only the affected keys, in the same transaction as the write. During a migration, specify `replaceExisting: true` on an individual `OnChange` to adopt an existing trigger with that exact name on that table.

## Registration and existing rows

For either example, call `database.RegisterEntities(typeof(Product))` at startup. Registration includes the referenced contracts, installs the triggers and populates existing rows when it installs or replaces a rule. For the VAT example, it calculates the category multipliers before filling the product prices. Repeating registration keeps unchanged triggers in place. [Schema Synchronization](Database-SchemaSynchronization.md)

## Related topics

[Data Annotations](Entitites-DataAnnotations.md) · [Relationships](Entitites-Relationships.md) · [Transactions](Database-Transactions.md)
