using Blueprint.Database;
using Blueprint.Database.Sql;
using Blueprint.Database.TransactionFactory;
using Blueprint.Fundamentals;
using Blueprint.Entities.Annotations;
using Blueprint.Entities;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace Blueprint.Tests.Tutorial1
{
    internal class EntitiesTutorial : BlueprintTest
    {

        private BlueprintDatabase? fDatabase = null;
        private readonly BlueprintLanguage fEnglishLanguage = new BlueprintLanguage(1, String.Empty); // English is the default language with no suffix
        private readonly BlueprintLanguage fGreekLanguage = new BlueprintLanguage(2, "GR");  // Greek has the suffix GR for multilingual columns

        /// <summary>Initializes the shop, creates a user and basket, and purchases an iPhone with an atomic stock update.</summary>
        /// <exception cref="Exception">Database initialization, sample creation, checkout, or shutdown fails.</exception>
        public override void RunTest()
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

                // RunTest is synchronous; wait here, outside any database operation or callback.
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
        private Product InsertProductsToDatabase()
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
        private WebsiteUser InsertWebsiteUserToDatabase()
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
        private void AddProductToBasket(long websiteUserID, long productID, int quantity)
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
        private async Task<Order> CheckoutAsync(long websiteUserID)
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
