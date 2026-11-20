# Schema Synchronization

`RegisterEntities` compares the registered model with SQL Server and applies supported schema changes immediately. Blueprint does not require a separate EF migration command or migration-history table for this synchronization.

## Trigger synchronization

```csharp
database.RegisterEntities(typeof(Product));
```

The database constructor creates a missing database. Registration then prepares the entity tables, related contracts, indexes, ID sequences, and any configured RAM caches. A repeated registration checks the model again.

Perform registration during application startup so that schema failures are handled before serving requests. It can also run after database initialization, but not inside an application transaction.

## Supported changes

| Change | Behavior |
| --- | --- |
| Missing schema, table, column, or declared index | Created when the model is valid and SQL permissions allow it. |
| New nullable column | Added without inventing values for existing rows. |
| New required column on a populated table | Requires an explicit usable default; otherwise registration fails. |
| Wider string or numeric type | Applied when existing values can be preserved, such as a larger maximum length or `int` to `bigint`. |
| Decimal precision or scale | Applied only when the new definition preserves the original integral and fractional capacity. |
| Making a column nullable | Supported. |
| Making a column required | Requires that existing rows contain no `NULL` values. |
| Default constraint changes | Synchronized for future inserts; they are not a general data backfill. |
| Missing or outdated ID sequence | Repaired so that generated IDs can continue beyond stored IDs. |

For example, changing `[MaxLength(100)]` to `[MaxLength(200)]` is a widening change. Reducing it to 50 requires an explicit migration strategy, even when today's rows happen to fit.

## Changes that need a migration

Blueprint does not automatically drop removed tables or columns. It does not infer renames, discard data, shrink incompatible types, or rewrite computed columns to match arbitrary changes.

For a C# rename that should preserve the existing SQL column, use `[Column("ExistingColumnName")]`. For an actual database rename or data transformation, prepare an explicit migration before registering the new model.

A removed column can still affect writes: an old required column without a usable default may reject inserts even though it is no longer in the class. Include retained columns and constraints in your migration review.

## Atomicity and failures

Schema changes are protected by a SQL transaction and an application lock, with table locking where needed. If synchronization fails, schema changes in that transaction are rolled back. Blueprint retains the previous active model and removes all new registrations from the failed batch.

RAM-cache prerequisites include database-level Change Tracking and snapshot-isolation settings. These can be enabled before the schema transaction and remain enabled if a later step fails. See [RAM Caching](Database-RAMCaching.md#sql-server-requirements).

Registration requires appropriate SQL permissions, including creation or alteration of objects when needed. It can fail for incompatible existing data, duplicate values in a proposed unique index, invalid relationships, or unsupported type changes. Resolve the reported model or data problem before retrying.

## Entity count triggers

A public static method marked `[EntityTriggers]` receives an `EntityTriggerBuilder<TSource>`. For example, this method on an inventory item counts rows with a non-null owner and includes only items that are listed.

```csharp
[EntityTriggers]
public static void ConfigureTriggers(EntityTriggerBuilder<Item> triggers)
{
    triggers.MaintainCount<Owner>("TR_Items_OwnerCount", item => item.OwnerID, owner => owner.ItemCount, item => item.IsListed);
}
```

Import `Blueprint.Entities.Annotations` and `Blueprint.Entities.Triggers`. Use a mapped foreign key to the target ID and a stored `int` counter. Declare the counter with `[DefaultValue(0), DatabaseGenerated(DatabaseGeneratedOption.Computed)]` when the target may also be registered on its own. With these attributes, the target can expose the counter as `public abstract int ItemCount { get; }`. The source rule otherwise configures the zero default and database-generated value automatically. Each counter has one owner rule. The optional predicate supports direct non-nullable Boolean, enum and integer properties, numeric or enum literals, comparisons, and Boolean AND, OR and NOT. Unsupported expressions fail registration.

SQL Server installs the trigger and initializes existing counts in the same schema transaction. Regular inserts, updates and deletes from any client adjust counters for the affected keys, including multi-row statements and transactions that roll back. Moving a row between owners adjusts both counters. An unchanged trigger is retained. Synchronization replaces changed or disabled Blueprint count triggers and removes obsolete Blueprint count triggers on registered source tables. Unmanaged triggers are preserved. Bulk-copy tools must fire triggers to participate; operations that bypass triggers, such as truncating the source table, require a recount.

## Generated IDs

Every entity has one `long` ID regardless of its language. IDs use per-table SQL sequences. Do not assign IDs to new entities, depend on contiguous values, or treat ID order as transaction commit order. See [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md#ids).

## Related topics

[SQL Server](Database-SqlDatabase.md) · [Data Annotations](Entitites-DataAnnotations.md) · [RAM Caching](Database-RAMCaching.md) · [Troubleshooting](Troubleshooting.md)
