using Blueprint.Database;
using Blueprint.Entities;
using Blueprint.Database.Sql;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using System.Diagnostics;

namespace Blueprint.Tests.Performance
{
    /// <summary>Μετρά συνολικά την αρχικοποίηση της BlueprintDatabase και την ανάγνωση των Products της DemoEShop από RAM.</summary>
    internal sealed class RAMCachePerformanceTest : BlueprintTest
    {
        private const string DATABASE_NAME = "DemoEShop";
        private const string TABLE_NAME = "dbo.website_ecommerce_products";

        /// <summary>Αρχικοποιεί τη βάση, καταχωρίζει το Product και εκτελεί Where με πλήρη φόρτωση αποτελεσμάτων σε μία χρονομέτρηση.</summary>
        /// <exception cref="SqlException">Δεν είναι διαθέσιμη η βάση ή αποτυγχάνει η αρχικοποίηση και φόρτωση της cache.</exception>
        /// <exception cref="InvalidOperationException">Λείπει ο πίνακας ή το πλήθος των cached Products διαφέρει από τη βάση.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(fDatabaseConnectionString)
            {
                InitialCatalog = DATABASE_NAME
            };
            using (SqlConnection connection = new SqlConnection(settings.ConnectionString))
            {
                connection.Open();
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT OBJECT_ID(@table, N'U');";
                    command.Parameters.AddWithValue("@table", TABLE_NAME);
                    Assert(command.ExecuteScalar() is int, $"The existing table {DATABASE_NAME}.{TABLE_NAME} is required.");
                }

                List<BlueprintLanguage> languages = new List<BlueprintLanguage>
                {
                    new BlueprintLanguage(1, string.Empty),
                    new BlueprintLanguage(2, "EN")
                };

                Console.WriteLine($"RAM cache performance: {DATABASE_NAME}.{TABLE_NAME}");
                Console.WriteLine("Initializing BlueprintDatabase and reading all products from RAM with Product.Where...");

               
                using (BlueprintDatabase database = new SqlDatabase(DATABASE_NAME, settings.ConnectionString, languages))
                {
                    database.RAMCacheRefreshInterval = TimeSpan.FromHours(1);

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    database.RegisterEntity<Product>();
                    stopwatch.Stop();

                    List<Product> products = Product.Where(product => true).ToList();
               

                    long storedProducts;
                    using (SqlCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT_BIG(*) FROM [dbo].[website_ecommerce_products];";
                        storedProducts = (long)command.ExecuteScalar()!;
                    }
                    Assert(products.Count == storedProducts, "The RAM query must return every product. Concurrent SQL writes can change the count during this test.");

                    Console.WriteLine($"Products read from RAM: {products.Count:N0}");
                    Console.WriteLine($"BlueprintDatabase initialization and Product.Where: {stopwatch.Elapsed.TotalMilliseconds:N2} ms ({stopwatch.Elapsed.TotalSeconds:N3} seconds)");
                    Console.WriteLine("Timing includes database construction, registration/schema checks, initial cache loading, and Product.Where(...).ToList(); excludes result verification.");
                    Console.WriteLine("SQL Server may already have data pages cached. No product rows are written by this test.");
                }
            }
        }
    }
}
