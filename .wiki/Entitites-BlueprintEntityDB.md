# BlueprintEntityDB

`BlueprintEntityDB` is the base class for entities that read from and write to SQL Server. Applications use the shared Entity API on the registered type, for example `Product.SingleOrDefault(...)`. See [BlueprintEntity](Entitites-BlueprintEntity.md).

## When to use BlueprintEntityDB

Choose this base when a query should see the values visible to its SQL transaction, or when loading the entire table into application memory would be expensive. A product catalog with many rows is a typical example. SQL Server can use a unique SKU index to find the one product a customer selected.

## Define and register a DB entity

This example maps `Product` to a separate example table and gives each product a unique SKU.

```csharp
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using System.ComponentModel.DataAnnotations;

/// <summary>Stores a catalog product with a unique SKU and a price.</summary>
[Entity("GuideProducts")]
[EntityIndex("UX_GuideProducts_SKU", nameof(SKU), IsUnique = true)]
public abstract class Product : BlueprintEntityDB
{
    [MaxLength(64)]
    public abstract string SKU { get; set; }

    public abstract decimal Price { get; set; }
}
```

Register the contract during startup, using the application's initialized `BlueprintDatabase`. Registration prepares the table and index. Product rows are read when a query executes. See [SQL Server setup](Database-SqlDatabase.md#register-entity-contracts).

```csharp
database.RegisterEntities(typeof(Product));
```

## Read and write data

To change the price of an existing product, look it up by SKU and explicitly save the new price.

```csharp
Product? product = Product.SingleOrDefault(item => item.SKU == "HP-001");
if (product != null)
{
    product.Price = 79.90m;
    Product.Update(product);
}
```

`SingleOrDefault` executes the lookup in SQL. Assigning `Price` changes the object in memory, and `Update` writes its mapped values to SQL. If the SKU is absent, the example skips the write. For filtering, paging, inserts, and deletes, see [Queries and CRUD](Entitites-BlueprintEntity.md#queries-and-crud).

## Related entities

Suppose a DB-backed `Product` includes a `Category` derived from `BlueprintEntityRAM` through `[AutoInclude]`. Outside a transaction, Blueprint reads the product from SQL and loads the category from its RAM snapshot. A DB-backed product included by a RAM entity still comes from SQL. Inside a Blueprint transaction, both come from SQL. See the [Product and Category relationship example](Database-QueryExecutors.md#a-db-product-with-a-ram-category) and the [BlueprintEntityRAM guide](Entitites-BlueprintEntityRAM.md).
