# Relationships

Relationships connect entity contracts through their IDs. Define a scalar foreign key and a reference property; add an inverse collection when the application needs it.

## Define a brand and its products

This example uses a simplified version of the [Product contract](Database-EShopCheckoutTutorial.md#define-a-contract) with an additional brand relationship. Keep one `Product` definition in your application.

```csharp
using Blueprint.Entities.Annotations;
using Blueprint.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueprintManual
{
    /// <summary>Describes a brand and its related products.</summary>
    [Entity("Brands")]
    public abstract class Brand : BlueprintEntityDB
    {
        [MaxLength(200)]
        public abstract string Name { get; set; }

        [InverseProperty(nameof(Product.Brand))]
        public abstract ICollection<Product> Products { get; set; }
    }

    /// <summary>Describes a product with an optional brand.</summary>
    [Entity("Products")]
    [EntityIndex("UX_Products_SKU", nameof(SKU), IsUnique = true)]
    public abstract class Product : BlueprintEntityDB
    {
        [MaxLength(64)]
        public abstract string SKU { get; set; }

        [Multilingual, MaxLength(200)]
        public abstract string? Title { get; set; }

        [Precision(18, 2)]
        public abstract decimal Price { get; set; }

        public abstract long? BrandID { get; set; }

        [ForeignKey(nameof(BrandID)), AutoInclude]
        public abstract Brand? Brand { get; set; }
    }
}
```

`BrandID` is nullable because the relationship is optional. For a required relationship, use `long` and a non-nullable reference. `[ForeignKey]` names properties on the dependent entity. Without it, the expected key name is the reference property name followed by `ID`.

Register `Product`; Blueprint discovers its related `Brand` contract. Explicitly registering both is also supported.

## Load related data

```csharp
Product? product = Product.SingleOrDefault(item => item.ID == productID);
Console.WriteLine(product?.Brand?.Name);
```

`AutoInclude` on `Product.Brand` loads the reference when the product is materialized. An inverse collection defines the relationship; it does not by itself request automatic loading. Add `AutoInclude` only to navigations that you need automatically, avoiding cycles and unnecessarily large object graphs.

Each `AutoInclude` target selects its own executor from its base class: DB targets read SQL and RAM targets read the synchronized cache, regardless of the parent's source. This works for both references and collections and continues through nested relationships. `AutoInclude` does not enable caching for DB classes. The query's selected language also applies to related entities, and relationships use the same ID across languages. Inside a transaction, the entire graph reads SQL. See [Query Executors](Database-QueryExecutors.md) for examples, query evaluation, and consistency.

## Insert a parent and child together

The transaction keeps operation order. Insert the new parent first and assign its instance to the child's mapped navigation:

```csharp
using Blueprint.Database.TransactionFactory;

Brand brand = Brand.Create();
brand.Name = "Example Brand";

Product product = Product.Create();
product.SKU = "CAMERA-BRAND-001";
product.Title = "Camera";
product.Price = 120m;
product.Brand = brand;

DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
transaction.AddInsert(brand);
transaction.AddInsert(product);
await database.TransactionFactory.ExecuteTransactionAsync(transaction);
Console.WriteLine($"Brand: {brand.ID}, product foreign key: {product.BrandID}");
```

Blueprint assigns the parent's generated ID to the child's foreign key before inserting the child. The parent must already exist or be inserted earlier in this transaction. A conflicting foreign-key value and navigation reference are rejected.

For an existing parent, you can set the scalar foreign key to its ID. Merely adding an object to `brand.Products` does not save it: enqueue or save each changed entity explicitly.

## Updates and deletes

Updating a product does not automatically update its brand or the entire navigation graph. Register each intended change as a separate operation.

Relationships use restricted delete behavior. Remove or reassign dependent rows before deleting a referenced parent. In a transaction, add child deletes before the parent delete. A foreign-key failure rolls back the transaction.

## Related topics

[Data Annotations](Entitites-DataAnnotations.md) · [Queries and CRUD](Entitites-BlueprintEntity.md#queries-and-crud) · [Transactions](Database-Transactions.md) · [Multilingual Entities](Entitites-MultilingualEntities.md)
