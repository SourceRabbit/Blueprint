# EShop - Checkout Tutorial

An entity is a public, non-sealed class that derives from `BlueprintEntityDB` or `BlueprintEntityRAM` in `Blueprint.Entities` and has an `[Entity]` annotation. Blueprint generates a concrete subclass when the contract is registered.

This guide follows [EntitiesTutorial](../Visual%20Studio%20Project/Blueprint.Tests/Tutorial1/EntitiesTutorial.cs): create a website user, add one iPhone to the user's basket, and complete checkout. Checkout saves an order and its items, reduces product stock, and clears the purchased basket in one database transaction.

Before writing code, read [Contributing](Contributing.md). Use [Getting Started](Getting-Started.md) to reference Blueprint from a .NET 10 application using C# 14. Database setup and connection settings are explained in [SQL Server](Database-SqlServer.md).

## Define a contract

The tutorial keeps the existing Product contract, including multilingual content and integer stock, and adds a concurrency token:

```csharp
/// <summary>Describes a product with multilingual content, a price, and stock quantity.</summary>
[Entity("Products")]
public abstract class Product : BlueprintEntityRAM
{
    public abstract string SKU { get; set; }

    [Multilingual]
    public abstract string Title { get; set; }

    [Multilingual]
    public abstract string ShortDescription { get; set; }

    [Multilingual]
    public abstract string Description { get; set; }

    [Precision(18, 2)]
    public abstract decimal Price { get; set; }

    public abstract int Stock { get; set; }

    [Timestamp]
    public abstract byte[] Version { get; set; }
}
```

`[Entity("Products")]` maps this contract to `dbo.Products`. `[Multilingual]` stores separate English and Greek values for the three text properties; `SKU`, `Price`, and `Stock` are shared. `[Precision(18, 2)]` configures the price's decimal precision. Deriving from `BlueprintEntityRAM` enables caching for eligible Product queries.

`[Timestamp]`, from `System.ComponentModel.DataAnnotations`, maps `Version` to a SQL Server `rowversion` concurrency token. SQL Server generates the value on insert and changes it whenever the row is updated. It is not a date or time. Blueprint checks the version read by the consumer when saving an update, so a stale entity cannot silently overwrite a newer version. RAM caching alone does not provide this protection.

This tutorial declares `Version` on both `Product` and `BasketItem`, the entities that checkout updates or deletes. The token is not inherited from `BlueprintEntity`. See [Concurrency](#concurrency) for an example and conflict handling.

All five contracts in the tutorial inherit these properties:

| Property | Meaning |
| --- | --- |
| `ID` | A `long` primary key. A new entity starts with zero; insertion supplies its generated ID. |
| `CurrentLanguage` | The configured language ID used when reading or assigning multilingual properties. It is not a stored column. |

Use `Product.Create()` or the corresponding contract's `Create()` method to obtain an unsaved instance. Assigning properties changes that object; an explicit insert or update persists it.

## The entities and their relationships

| Entity | SQL table | Data and purpose |
| --- | --- | --- |
| `WebsiteUser` | `WebsiteUsers` | The customer: `FirstName`, `LastName`, and `Email`. |
| `Product` | `Products` | The existing iPhones, their descriptions, current prices, and available stock. |
| `BasketItem` | `BasketItems` | One user's selected product and quantity, identified by `WebsiteUserID` and `ProductID`. |
| `Order` | `Orders` | A completed purchase: `WebsiteUserID`, `CreatedAtUtc`, and `Total`. |
| `OrderItem` | `OrderItems` | A purchased product: `OrderID`, `ProductID`, `Quantity`, and `UnitPrice`. |

A user's current basket consists of the `BasketItem` rows with that user's ID. This example does not need a separate Basket entity. Each order belongs to a user and can have several order items.

The scalar properties such as `WebsiteUserID` store foreign keys. Navigation properties such as `WebsiteUser` refer to related entities, and `[ForeignKey(nameof(WebsiteUserID))]` identifies the matching key. These are required relationships because the key properties are non-nullable `long` values. Setting a scalar ID is sufficient when the related row already exists.

The unique index on `BasketItem` allows only one row per `WebsiteUserID` and `ProductID` pair. Quantity belongs to that row. The tutorial helper inserts a new entry; adding the same product to the same basket again fails the uniqueness constraint rather than increasing its quantity.

## Complete example in Main

The following standalone `Program.cs` contains all five contracts and all helper methods. It adapts the test's `RunTest()` to `public static void Main()`, removes the `BlueprintTest` inheritance, and makes the shared fields and helpers static. Use this complete file once; the snippets in the walkthrough below explain excerpts from it.

The example connects to local `SQLEXPRESS` and uses `MyEshopDatabase`, English, and Greek. Configure the connection for your environment using the [SQL Server guide](Database-SqlServer.md). Product's `BlueprintEntityRAM` base class also requires the [SQL Server caching setup](Database-RAM-Caching.md#sql-server-requirements).

Each execution inserts three new sample products and a new user, then purchases the iPhone 15 inserted by that execution. The tutorial does not reuse an existing customer or deduplicate SKUs. Prices and quantities are demonstration values.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
﻿using Blueprint.Database;
using Blueprint.Database.Sql;
using Blueprint.Database.TransactionFactory;
using Blueprint.Fundamentals;
using Blueprint.Entities.Annotations;
using Blueprint.Entities;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace BlueprintManual
{
    internal class EntitiesTutorial
    {

        private static BlueprintDatabase? fDatabase = null;
        private static readonly BlueprintLanguage fEnglishLanguage = new BlueprintLanguage(1, String.Empty); // English is the default language with no suffix
        private static readonly BlueprintLanguage fGreekLanguage = new BlueprintLanguage(2, "GR");  // Greek has the suffix GR for multilingual columns

        /// <summary>Initializes the shop, creates a user and basket, and purchases an iPhone with an atomic stock update.</summary>
        /// <exception cref="Exception">Database initialization, sample creation, checkout, or shutdown fails.</exception>
        public static void Main()
        {
            // Step 1 - Configure the database connection.
            string sqlServerConnectionString = "Server=.\\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";

            // Step 2 - Initialize the languages used by the application.
            // The default language has no suffix and is used for all non-multilingual columns.
            // Multilingual columns have a suffix for each language.
            List<BlueprintLanguage> languages = new List<BlueprintLanguage>();
            languages.Add(fEnglishLanguage);
            languages.Add(fGreekLanguage);

            // Step 3 - Initialize the database and register the entities used by the application.
            // The database schema will be created for these entities.
            BlueprintDatabase db = new SqlDatabase("MyEshopDatabase", sqlServerConnectionString, languages);
            db.RAMCacheRefreshInterval = TimeSpan.FromSeconds(5); 

            using (db)
            {
                // Register the entities used by the shop.
                db.RegisterEntity<Product>();
                db.RegisterEntity<WebsiteUser>();
                db.RegisterEntity<BasketItem>();
                db.RegisterEntity<Order>();
                db.RegisterEntity<OrderItem>();

                fDatabase = db;

                // Step 4 - Insert sample iPhones into the database.
                Product iphone15 = InsertProductsToDatabase();

                // Step 5 - Create a user and add one iPhone to the basket without reserving stock.
                WebsiteUser user = InsertWebsiteUserToDatabase();
                AddProductToBasket(user.ID, iphone15.ID, 1);
                Console.WriteLine($"User: {user.FirstName} {user.LastName} ({user.Email})");
                Console.WriteLine($"Basket: {iphone15.SKU} x 1");
                Console.WriteLine($"Stock before checkout: {iphone15.Stock}");

                // Main is synchronous; wait here, outside any database operation or callback.
                Order order = CheckoutAsync(user.ID).GetAwaiter().GetResult();

                Product storedProduct = Product.SingleOrDefault(item => item.ID == iphone15.ID) ?? throw new InvalidOperationException("The purchased product was not found.");
                int remainingBasketItems = BasketItem.Where(item => item.WebsiteUserID == user.ID).Count();
                Console.WriteLine($"Order #{order.ID} completed. Total: {order.Total:0.00}");
                Console.WriteLine($"Stock after checkout: {storedProduct.Stock}");
                Console.WriteLine($"Basket items after checkout: {remainingBasketItems}");
            }
        }

        /// <summary>Creates and saves three sample iPhones with both translations, prices, and stock quantities.</summary>
        /// <remarks>Prices and stock quantities are demonstration values. Each call inserts new rows.</remarks>
        /// <returns>The iPhone 15 inserted by this run, so checkout does not select an older row with the same SKU.</returns>
        /// <exception cref="InvalidOperationException">Blueprint or the Product entity has not been initialized.</exception>
        /// <exception cref="System.Data.Common.DbException">Communication with the database fails.</exception>
        /// <exception cref="DbUpdateException">The products cannot be saved.</exception>
        private static Product InsertProductsToDatabase()
        {
            Product iphone15 = Product.Create();
            iphone15.SKU = "IPHONE-15-128-BLACK";
            iphone15.Price = 699.00m;
            iphone15.Stock = 20;
            // English
            iphone15.CurrentLanguage = fEnglishLanguage.ID;
            iphone15.Title = "iPhone 15 128GB Black";
            iphone15.ShortDescription = "Apple iPhone 15 with 128GB storage in black.";
            iphone15.Description = "Apple iPhone 15 in black with 128GB of storage for apps, photos, and videos.";
            // Greek
            iphone15.CurrentLanguage = fGreekLanguage.ID;
            iphone15.Title = "iPhone 15 128GB Μαύρο";
            iphone15.ShortDescription = "Apple iPhone 15 με αποθηκευτικό χώρο 128GB σε μαύρο χρώμα.";
            iphone15.Description = "Apple iPhone 15 σε μαύρο χρώμα με αποθηκευτικό χώρο 128GB για εφαρμογές, φωτογραφίες και βίντεο.";
            Product.Insert(iphone15); // Insert the product into the database

            Product iphone15Plus = Product.Create();
            iphone15Plus.SKU = "IPHONE-15-PLUS-128-BLUE";
            iphone15Plus.Price = 799.00m;
            iphone15Plus.Stock = 12;
            // English
            iphone15Plus.CurrentLanguage = fEnglishLanguage.ID;
            iphone15Plus.Title = "iPhone 15 Plus 128GB Blue";
            iphone15Plus.ShortDescription = "Apple iPhone 15 Plus with 128GB storage in blue.";
            iphone15Plus.Description = "Apple iPhone 15 Plus in blue with 128GB of storage for apps, photos, and videos.";
            // Greek
            iphone15Plus.CurrentLanguage = fGreekLanguage.ID;
            iphone15Plus.Title = "iPhone 15 Plus 128GB Μπλε";
            iphone15Plus.ShortDescription = "Apple iPhone 15 Plus με αποθηκευτικό χώρο 128GB σε μπλε χρώμα.";
            iphone15Plus.Description = "Apple iPhone 15 Plus σε μπλε χρώμα με αποθηκευτικό χώρο 128GB για εφαρμογές, φωτογραφίες και βίντεο.";
            Product.Insert(iphone15Plus); // Insert the product into the database

            Product iphone16 = Product.Create();
            iphone16.SKU = "IPHONE-16-256-BLACK";
            iphone16.Price = 999.00m;
            iphone16.Stock = 8;
            // English
            iphone16.CurrentLanguage = fEnglishLanguage.ID;
            iphone16.Title = "iPhone 16 256GB Black";
            iphone16.ShortDescription = "Apple iPhone 16 with 256GB storage in black.";
            iphone16.Description = "Apple iPhone 16 in black with 256GB of storage for apps, photos, and videos.";
            // Greek
            iphone16.CurrentLanguage = fGreekLanguage.ID;
            iphone16.Title = "iPhone 16 256GB Μαύρο";
            iphone16.ShortDescription = "Apple iPhone 16 με αποθηκευτικό χώρο 256GB σε μαύρο χρώμα.";
            iphone16.Description = "Apple iPhone 16 σε μαύρο χρώμα με αποθηκευτικό χώρο 256GB για εφαρμογές, φωτογραφίες και βίντεο.";
            Product.Insert(iphone16); // Insert the product into the database

            return iphone15;
        }

        /// <summary>Creates and saves the customer used by this tutorial run.</summary>
        /// <returns>The saved website user with its generated ID.</returns>
        /// <exception cref="InvalidOperationException">Blueprint or the WebsiteUser entity has not been initialized.</exception>
        /// <exception cref="DbUpdateException">The user cannot be saved.</exception>
        private static WebsiteUser InsertWebsiteUserToDatabase()
        {
            WebsiteUser user = WebsiteUser.Create();
            user.FirstName = "John";
            user.LastName = "Smith";
            user.Email = "john.smith@example.com";
            WebsiteUser.Insert(user);
            return user;
        }

        /// <summary>Adds a product to the user's basket without changing its available stock.</summary>
        /// <param name="websiteUserID">The ID of the saved customer.</param>
        /// <param name="productID">The ID of the saved product.</param>
        /// <param name="quantity">The positive number of units to purchase.</param>
        /// <exception cref="ArgumentOutOfRangeException">The quantity is not positive.</exception>
        /// <exception cref="DbUpdateException">The user or product does not exist, the product is already in this basket, or saving fails.</exception>
        private static void AddProductToBasket(long websiteUserID, long productID, int quantity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
            BasketItem basketItem = BasketItem.Create();
            basketItem.WebsiteUserID = websiteUserID;
            basketItem.ProductID = productID;
            basketItem.Quantity = quantity;
            BasketItem.Insert(basketItem);
        }

        /// <summary>Converts the user's basket into an order, reduces stock, and clears the basket in one transaction.</summary>
        /// <param name="websiteUserID">The ID of the customer whose current basket will be purchased.</param>
        /// <returns>The order after its transaction has committed.</returns>
        /// <exception cref="InvalidOperationException">Blueprint is unavailable, data is missing, the basket is empty, a quantity is invalid, or stock is insufficient.</exception>
        /// <exception cref="DbUpdateException">An order, stock update, or basket deletion cannot be saved.</exception>
        /// <exception cref="DbUpdateConcurrencyException">A product or basket entry changed since it was read, or a queued update or delete targets a missing row.</exception>
        /// <exception cref="System.Data.Common.DbException">Communication with SQL Server or transaction execution fails.</exception>
        private static async Task<Order> CheckoutAsync(long websiteUserID)
        {
            BlueprintDatabase database = fDatabase ?? throw new InvalidOperationException("Blueprint has not been initialized.");
            Order? order = null;
            DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction(pending =>
            {
                WebsiteUser user = WebsiteUser.SingleOrDefault(item => item.ID == websiteUserID) ?? throw new InvalidOperationException("The website user was not found.");
                List<BasketItem> basketItems = BasketItem.Where(item => item.WebsiteUserID == user.ID).OrderBy(item => item.ProductID).ToList();
                if (basketItems.Count == 0)
                {
                    throw new InvalidOperationException("The basket is empty.");
                }

                order = Order.Create();
                order.WebsiteUserID = user.ID;
                order.CreatedAtUtc = DateTime.UtcNow;
                order.Total = 0m;
                pending.AddInsert(order);

                foreach (BasketItem basketItem in basketItems)
                {
                    if (basketItem.Quantity <= 0)
                    {
                        throw new InvalidOperationException("Basket quantities must be positive.");
                    }

                    // Transaction queries read SQL directly, including products derived from BlueprintEntityRAM.
                    Product product = Product.SingleOrDefault(item => item.ID == basketItem.ProductID) ?? throw new InvalidOperationException("A basket product was not found.");
                    if (product.Stock < basketItem.Quantity)
                    {
                        throw new InvalidOperationException($"Insufficient stock for {product.SKU}. Requested: {basketItem.Quantity}; available: {product.Stock}.");
                    }

                    OrderItem orderItem = OrderItem.Create();
                    // Blueprint fills OrderID after the earlier order insert assigns its generated ID.
                    orderItem.Order = order;
                    orderItem.ProductID = product.ID;
                    orderItem.Quantity = basketItem.Quantity;
                    orderItem.UnitPrice = product.Price;
                    order.Total += orderItem.UnitPrice * orderItem.Quantity;
                    pending.AddInsert(orderItem);

                    product.Stock -= basketItem.Quantity;
                    pending.AddUpdate(product);
                    pending.AddDelete(basketItem);
                }
            });

            await database.TransactionFactory.ExecuteTransactionAsync(transaction, IsolationLevel.Serializable);
            return order ?? throw new InvalidOperationException("Checkout completed without an order.");
        }

    }

    /// <summary>Describes a product with multilingual content, a price, and stock quantity.</summary>
    [Entity("Products")]
    public abstract class Product : BlueprintEntityRAM
    {
        public abstract string SKU { get; set; }

        [Multilingual]
        public abstract string Title { get; set; }

        [Multilingual]
        public abstract string ShortDescription { get; set; }

        [Multilingual]
        public abstract string Description { get; set; }

        [Precision(18, 2)]
        public abstract decimal Price { get; set; }

        public abstract int Stock { get; set; }

        [Timestamp]
        public abstract byte[] Version { get; set; }
    }

    /// <summary>Defines a website user for the checkout tutorial.</summary>
    [Entity("WebsiteUsers")]
    public abstract class WebsiteUser : BlueprintEntityDB
    {
        public abstract string FirstName { get; set; }

        public abstract string LastName { get; set; }

        public abstract string Email { get; set; }
    }

    /// <summary>Stores one basket entry per user and product, without reserving stock.</summary>
    [Entity("BasketItems")]
    [EntityIndex("UX_BasketItems_WebsiteUserID_ProductID", nameof(WebsiteUserID), nameof(ProductID), IsUnique = true)]
    public abstract class BasketItem : BlueprintEntityDB
    {
        public abstract long WebsiteUserID { get; set; }

        [ForeignKey(nameof(WebsiteUserID))]
        public abstract WebsiteUser WebsiteUser { get; set; }

        public abstract long ProductID { get; set; }

        [ForeignKey(nameof(ProductID))]
        public abstract Product Product { get; set; }

        public abstract int Quantity { get; set; }

        [Timestamp]
        public abstract byte[] Version { get; set; }
    }

    /// <summary>Records a customer's completed purchase and its total at checkout.</summary>
    [Entity("Orders")]
    public abstract class Order : BlueprintEntityDB
    {
        public abstract long WebsiteUserID { get; set; }

        [ForeignKey(nameof(WebsiteUserID))]
        public abstract WebsiteUser WebsiteUser { get; set; }

        public abstract DateTime CreatedAtUtc { get; set; }

        [Precision(18, 2)]
        public abstract decimal Total { get; set; }
    }

    /// <summary>Stores a purchased product, its quantity, and the unit price agreed at checkout.</summary>
    [Entity("OrderItems")]
    public abstract class OrderItem : BlueprintEntityDB
    {
        public abstract long OrderID { get; set; }

        [ForeignKey(nameof(OrderID))]
        public abstract Order Order { get; set; }

        public abstract long ProductID { get; set; }

        [ForeignKey(nameof(ProductID))]
        public abstract Product Product { get; set; }

        public abstract int Quantity { get; set; }

        [Precision(18, 2)]
        public abstract decimal UnitPrice { get; set; }
    }
}
```

Run the console application with `dotnet run`. The following sections explain the definitions and operations used by this program.

## How the steps work

### 1. Initialize languages, register entities, and activate Blueprint

The private static language fields define English with an empty prefix and Greek with the `GR` prefix. Add both to the language list passed to `SqlDatabase`:

```csharp
List<BlueprintLanguage> languages = new List<BlueprintLanguage>();
languages.Add(fEnglishLanguage);
languages.Add(fGreekLanguage);
```

`SqlDatabase` creates the database if needed. Setting `RAMCacheRefreshInterval` to five seconds configures cache polling; it does not delay writes or reserve stock.

Inside `using (db)`, register the contracts and keep the database reference for transactions:

```csharp
db.RegisterEntity<Product>();
db.RegisterEntity<WebsiteUser>();
db.RegisterEntity<BasketItem>();
db.RegisterEntity<Order>();
db.RegisterEntity<OrderItem>();

fDatabase = db;
```

Registration synchronizes the tables and relationships, adds the `Version` rowversion columns to Products and BasketItems if missing, and prepares the Product cache. The database constructor activates the Entity API. All registration happens before creating the sample entity instances.

### 2. Create the sample iPhones and keep the selected product's ID

```csharp
Product iphone15 = InsertProductsToDatabase();
```

`InsertProductsToDatabase()` saves the three iPhones with English and Greek content. It returns the newly saved iPhone 15, whose initial price is `699.00m` and stock is `20`.

The generated `iphone15.ID` identifies the exact row purchased in this run. Looking up the product by SKU alone could be ambiguous after running the tutorial multiple times because this Product contract does not make SKU unique.

Changing `CurrentLanguage` while assigning the sample descriptions stores both translations on the same product. Stock remains one shared quantity for all languages.

### 3. Create and save WebsiteUser

`InsertWebsiteUserToDatabase()` creates John Smith and saves the user:

```csharp
WebsiteUser user = WebsiteUser.Create();
user.FirstName = "John";
user.LastName = "Smith";
user.Email = "john.smith@example.com";
WebsiteUser.Insert(user);
```

`WebsiteUser.Create()` returns an unsaved generated implementation. After `WebsiteUser.Insert(user)`, its `ID` is populated. The returned user supplies the customer ID for both basket entries and orders. No order or stock change is made by creating the user.

### 4. Add one iPhone to the basket

The entry point calls `AddProductToBasket(user.ID, iphone15.ID, 1)`. Its body validates the quantity and saves a basket entry:

```csharp
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
BasketItem basketItem = BasketItem.Create();
basketItem.WebsiteUserID = websiteUserID;
basketItem.ProductID = productID;
basketItem.Quantity = quantity;
BasketItem.Insert(basketItem);
```

`WebsiteUserID` identifies the basket owner, `ProductID` identifies the selected iPhone, and `Quantity = 1` records the requested units. The foreign keys require both referenced rows to exist.

At this point the basket has one row and the iPhone still has `Stock = 20`. Adding to the basket does not reserve inventory. Availability and the current product price will be read again during checkout.

### 5. Call checkout and wait for its result

```csharp
Order order = CheckoutAsync(user.ID).GetAwaiter().GetResult();
```

`CheckoutAsync()` returns a `Task<Order>`. This console example has a synchronous `Main()`, so it waits at this outer boundary. An asynchronous caller can await the same method. The call occurs outside any database operation or transaction callback.

Inside the method, `CreateNewTransaction(pending => ...)` stores a synchronous preparation callback. It does not execute that callback or write rows immediately. `ExecuteTransactionAsync` later opens the SQL transaction, runs preparation, executes the queued writes, and commits.

This placement matters: the reads used to decide whether checkout can proceed happen inside the same transaction as the writes.

### 6. Read the current user and basket inside the transaction

The preparation callback first reloads the user and reads only that user's basket:

```csharp
WebsiteUser user = WebsiteUser.SingleOrDefault(item => item.ID == websiteUserID) ?? throw new InvalidOperationException("The website user was not found.");
List<BasketItem> basketItems = BasketItem.Where(item => item.WebsiteUserID == user.ID).OrderBy(item => item.ProductID).ToList();
if (basketItems.Count == 0)
{
    throw new InvalidOperationException("The basket is empty.");
}
```

A missing user or an empty basket stops checkout. `Where(...)` describes the basket query and `ToList()` executes it. Sorting by `ProductID` gives the products a consistent processing order.

The callback checks every basket quantity, even though the helper already checked it when adding the entry. Persisted data may have changed since that earlier operation.

Each product is then read again and checked against the requested quantity:

```csharp
Product product = Product.SingleOrDefault(item => item.ID == basketItem.ProductID) ?? throw new InvalidOperationException("A basket product was not found.");
if (product.Stock < basketItem.Quantity)
{
    throw new InvalidOperationException($"Insufficient stock for {product.SKU}. Requested: {basketItem.Quantity}; available: {product.Stock}.");
}
```

Queries inside a Blueprint transaction use SQL, including queries for products derived from `BlueprintEntityRAM`. The decision therefore uses the stock read from SQL within this transaction, rather than a previously returned object or cached basket-screen value.

### 7. Prepare the order and preserve the purchase price

The callback creates an order for the user:

```csharp
order = Order.Create();
order.WebsiteUserID = user.ID;
order.CreatedAtUtc = DateTime.UtcNow;
order.Total = 0m;
pending.AddInsert(order);
```

`CreatedAtUtc` records the checkout time in UTC. `Total` starts at zero and accumulates the line totals as the basket is processed.

For each basket entry, it creates an order item:

```csharp
OrderItem orderItem = OrderItem.Create();
// Blueprint fills OrderID after the earlier order insert assigns its generated ID.
orderItem.Order = order;
orderItem.ProductID = product.ID;
orderItem.Quantity = basketItem.Quantity;
orderItem.UnitPrice = product.Price;
order.Total += orderItem.UnitPrice * orderItem.Quantity;
pending.AddInsert(orderItem);
```

`UnitPrice` copies the product price read during checkout. Later changes to `Product.Price` do not change this stored purchase price. The line total is `UnitPrice × Quantity`; buying one iPhone at `699.00` gives an order total of `699.00`.

The order is still unsaved while this callback runs. Assigning `orderItem.Order = order` gives Blueprint the parent reference. The queued order insert runs before its item inserts, so Blueprint can copy the generated `Order.ID` into each item's `OrderID`.

`AddInsert` queues the entity by reference. All preparation finishes before writes start, so the order insert saves the final accumulated `Total`, even though it was queued before the loop.

### 8. Queue the stock update and remove the purchased basket entry

```csharp
product.Stock -= basketItem.Quantity;
pending.AddUpdate(product);
pending.AddDelete(basketItem);
```

Subtracting the quantity changes the loaded Product object. `AddUpdate(product)` queues persistence of that new value. For this purchase, the calculation is `20 - 1 = 19`.

`AddDelete(basketItem)` queues removal of the purchased entry. It deletes the basket row, not the Product or WebsiteUser. With a one-item basket, successful checkout leaves it empty.

Both operations carry the entity's loaded `Version`. The update checks the product's token, and the delete checks the basket entry's token. Leave these values unchanged while preparing checkout; SQL Server generates the new product version when the update succeeds.

No direct `Product.Update(...)` or `BasketItem.Delete(...)` call is made in preparation. The queued operations allow all checkout writes to share one transaction.

### 9. Execute the transaction and commit the purchase

```csharp
await database.TransactionFactory.ExecuteTransactionAsync(transaction, IsolationLevel.Serializable);
return order ?? throw new InvalidOperationException("Checkout completed without an order.");
```

For the one-item basket, execution writes in this order:

| Operation | SQL effect |
| --- | --- |
| `AddInsert(order)` | Inserts the order with its user ID, date, and final total. |
| `AddInsert(orderItem)` | Inserts the line with the generated order ID, product ID, quantity, and purchase price. |
| `AddUpdate(product)` | Persists the stock reduction while retaining the loaded product data and translations. |
| `AddDelete(basketItem)` | Removes the purchased basket entry. |

The factory commits once, after all queued operations succeed. It returns an affected-write count; `CheckoutAsync()` then returns the saved Order object. The application does not call commit manually.

`IsolationLevel.Serializable` protects the data read for the checkout decision until the transaction completes. Its read locks also protect matching key ranges, which helps keep the selected basket stable during checkout. See Microsoft's [SQL Server isolation-level documentation](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql?view=sql-server-ver17) and the [concurrency notes below](#concurrency).

### 10. Read the result and release resources

After checkout returns, the program queries Product and BasketItem again:

```csharp
Product storedProduct = Product.SingleOrDefault(item => item.ID == iphone15.ID) ?? throw new InvalidOperationException("The purchased product was not found.");
int remainingBasketItems = BasketItem.Where(item => item.WebsiteUserID == user.ID).Count();
```

The original `iphone15` variable is an independent object and does not automatically receive changes made to another Product instance during checkout. Reading again obtains the current persisted stock. Blueprint invalidates cache state after transaction execution so subsequent cached reads synchronize committed data.

The program prints the order, the updated stock, and the number of remaining basket entries. The using block disposes the database even if registration or checkout throws. Disposal clears the active database and releases resources; saved rows remain in SQL Server.

## Expected result

For the user and iPhone created by this run:

| Data | Before checkout | After successful checkout |
| --- | --- | --- |
| WebsiteUser | Saved John Smith | The same saved user |
| iPhone 15 stock | `20` | `19` |
| User's basket | One entry with quantity `1` | Empty |
| Order | No order for this new user | One order with total `699.00` |
| OrderItem | No purchase line for this user | One line, quantity `1`, unit price `699.00` |
| Other sample products | iPhone 15 Plus stock `12`; iPhone 16 stock `8` | Unchanged |

Illustrative console output:

```text
User: John Smith (john.smith@example.com)
Basket: IPHONE-15-128-BLACK x 1
Stock before checkout: 20
Order #1 completed. Total: 699.00
Stock after checkout: 19
Basket items after checkout: 0
```

Generated IDs and decimal formatting can differ in your environment. The stored order and order item remain available after the basket entry has been deleted.

## Failure and rollback

The transaction makes the order, its items, the stock changes, and the basket deletions one atomic operation.

| Failure | Result |
| --- | --- |
| User or product is missing | Checkout throws; no purchase is committed. |
| Basket is empty or a quantity is not positive | Checkout throws during preparation, before queued writes execute. |
| Stock is below the requested quantity | Checkout throws; it does not reduce stock or remove the basket entry. |
| A product or basket entry fails its version check | `DbUpdateConcurrencyException` is propagated; Blueprint attempts rollback of all checkout writes. |
| An insert, stock update, or basket deletion fails during execution | Blueprint attempts rollback of all checkout writes, including earlier writes that succeeded. |

For example, if removing the basket entry fails after the stock update, a successful rollback removes the new order and order item, restores stock to its value before checkout, and keeps the basket entry. These statements describe the stored database state.

The three sample products, the user, and the original basket entry were inserted before checkout. They are outside its transaction and are not deleted when checkout fails.

Rollback snapshots are captured after preparation. They do not undo every earlier in-memory assignment such as the stock subtraction made during preparation. Reload entities before continuing after a failure. A lost connection during commit can also leave the outcome uncertain; see [transaction rollback and reuse](Database-Transactions.md#commit-rollback-and-reuse).

The example propagates checkout errors. Calling checkout again after a confirmed rollback creates a new transaction and rereads the basket and products; transaction objects cannot be submitted twice.

## Saving related changes

Relationships are not saved as an implicit graph. Each order, order item, product update, and basket deletion is explicitly queued. New parents must be inserted before dependent rows that reference them.

Direct Entity API calls, such as the initial `WebsiteUser.Insert(user)`, save explicitly. Use the transaction factory when several changes must succeed or fail together. There is no separate consumer `SaveChanges()` step. See [Relationships](Data-Entities-Relationships.md) and [Transactions](Database-Transactions.md).

## IDs

Blueprint uses a separate SQL sequence for each table. Leave a new entity's `ID` at zero; insertion assigns the generated value to that instance.

IDs can have gaps after rollback or deletion and are not row counts or guarantees of commit order. This is why the tutorial passes the actual inserted iPhone's ID and uses the new Order reference when preparing OrderItem, rather than guessing IDs.

## Concurrency

### Detect stale updates with Version

`Product.Version` and `BasketItem.Version` identify the version of each row that the consumer read. When saving, Blueprint uses this value to check whether another writer has updated that row since it was read. This is called optimistic concurrency.

Both contracts declare:

```csharp
[Timestamp]
public abstract byte[] Version { get; set; }
```

- `[Timestamp]`, from `System.ComponentModel.DataAnnotations`, configures the property as a database-generated concurrency token.
- `byte[]` holds the binary value generated by SQL Server. A SQL Server `rowversion` occupies eight bytes.
- `Version` is the property name and, in this example, the corresponding column name.

Despite the annotation's name, **this value is not a date or time**. SQL Server generates it when a row is inserted and changes it whenever that row is updated. The application does not calculate or increment it. The value identifies a version; it does not contain a history of previous values.

Reading an entity loads its token along with its other properties. For an existing product identified by `productID`, an ordinary update looks like this:

```csharp
Product product = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product was not found.");
product.Price = 649m;
Product.Update(product);
```

Change the business properties and **leave `product.Version` unchanged until saving**. Blueprint uses the token you read as the expected version in the database. The SQL condition is equivalent to this simplified example:

```sql
UPDATE Products
SET Price = @newPrice
WHERE ID = @productID
  AND Version = @versionRead;
```

This SQL illustrates the version check, not the complete generated statement: Blueprint updates all mapped writable values, including translations, rather than only the edited property. The important condition is that **both the ID and the version must match**. The comparison and write happen in the same SQL statement; there is no separate check that another writer can invalidate before the update.

If the version matches, SQL Server updates the row and generates a new version. After a successful `Product.Update(product)`, Blueprint also copies that new token into `product.Version`. If the stored version has changed, the update matches no row and Blueprint propagates `DbUpdateConcurrencyException` instead of overwriting the newer data. This also protects an entity originally read from RAM cache, because the write and version check happen in SQL Server.

For example, consider two servers editing the same product through ordinary `Product.Update(...)` calls:

| Step | Action | Database state or result |
| --- | --- | --- |
| 1 | Server A reads the product. | Price `699.00`, version V1. |
| 2 | Server B reads the same product. | Price `699.00`, version V1. |
| 3 | Server A saves price `649.00` using V1. | Success: price `649.00`, new version V2. |
| 4 | Server B attempts to save price `679.00` using V1. | `DbUpdateConcurrencyException`: the database already holds V2, so price `649.00` remains stored. |

V1 and V2 are illustrative labels; the actual tokens are `byte[]` values. The token covers the whole row, including stock and translations, so edits to different properties can also conflict. Load the entity before editing it and preserve its token until saving. Do not assign a new token or replace it with a newer value merely to force an old edit through.

The token does not keep the row locked while a consumer edits an object, and Blueprint does not automatically merge conflicting edits. The consumer must handle the exception by reloading current data and deciding whether to merge changes, retry the operation, or inform the user.

In checkout, the queued product update checks `Product.Version`, and the queued basket deletion checks `BasketItem.Version`. Let a failed transaction roll back; after a confirmed rollback, a new `CheckoutAsync()` call creates a new transaction and reevaluates the current basket, prices, and stock. This tutorial propagates the exception and does not retry automatically. See [Data Annotations: Concurrency](Database-Entities-Annotations.md#concurrency).

### Why Product needs Version: an administrator and an order

The token also protects against lost updates when two writers change different properties:

1. An administrator loads a product for editing. The loaded object contains `Stock = 20` and version V1.
2. A customer's checkout commits an order for one unit. The stored product now has `Stock = 19` and version V2.
3. The administrator changes only the product's description and saves the original object through `Product.Update(product)`. That object still contains `Stock = 20` and version V1.

**Without a concurrency token, saving the description can accidentally restore the old stock value of 20.** Blueprint updates all mapped writable values, so the stale stock value can overwrite the reduction made by checkout even though the administrator did not intend to change stock.

**With `Version`, the administrator's stale update fails with `DbUpdateConcurrencyException`, and stock remains 19.** The consumer can reload the current product, reapply only the intended description change while preserving the current stock and token, and attempt the update again. Any further concurrent change is checked on that attempt as well.

This protection is useful whether Product derives from `BlueprintEntityDB` or `BlueprintEntityRAM`. A cached read can return an older version until synchronization completes, and refreshing the cache does not update an object already loaded by the administrator. Keep using current SQL reads inside the checkout transaction for the final price and stock decision, as this tutorial does.

### Protect the checkout decision with transaction isolation

A database transaction alone does not make a stock decision based on an earlier read safe. This example both reads the basket and products inside preparation and explicitly requests `Serializable` isolation. The factory's default isolation is `ReadCommitted`.

The rowversion checks complement this isolation level. They detect stale individual entities; `Serializable` also protects the set of basket rows used for the checkout decision. Keep this isolation level even with `Version` present.

Blueprint's shared lock coordinates operations on one database instance. SQL isolation governs competing connections and processes. `Serializable` can block competing operations; concurrent read-then-update transactions can also encounter a deadlock. This example does not implement automatic retries. See [SQL Server locking and deadlocks](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-deadlocks-guide?view=sql-server-ver17) and [transaction thread safety and isolation](Database-Transactions.md#thread-safety-and-isolation).

Keep transaction callbacks synchronous and use the queued operations. Do not start asynchronous work or perform direct CRUD inside preparation. Do not share the mutable entity objects with concurrent writers while a transaction is executing.

## Related topics

[SQL Server](Database-SqlServer.md) · [Transactions](Database-Transactions.md) · [Relationships](Data-Entities-Relationships.md) · [Queries and CRUD](Database-Queries-and-CRUD.md) · [Data Annotations](Database-Entities-Annotations.md) · [Multilingual Entities](Database-Multilingual-Entities.md) · [RAM Caching](Database-RAM-Caching.md)
