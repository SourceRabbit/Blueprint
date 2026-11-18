using Blueprint.Database;
using Blueprint.Database.Entities;
using Blueprint.Database.Entities.Annotations;
using Blueprint.Database.Sql;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace Blueprint.Tests.RAMCache
{
    /// <summary>Ελέγχει cached AutoInclude references, συλλογές, κύκλους και συγχρονισμό σε απομονωμένη SQL βάση.</summary>
    internal sealed class AutoIncludeTest : BlueprintTest
    {
        /// <summary>Δημιουργεί προσωρινή βάση και ελέγχει τη σύνθεση των AutoInclude αποκλειστικά από cached στιγμιότυπα.</summary>
        /// <exception cref="Exception">Αποτυγχάνει SQL εντολή, assertion ή η διαγραφή της προσωρινής βάσης.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            string databaseName = "BlueprintAutoInclude_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(fDatabaseConnectionString) { InitialCatalog = "master", Pooling = false, ConnectTimeout = 5 };
            using SqlConnection master = new SqlConnection(settings.ConnectionString);
            master.Open();
            try
            {
                using BlueprintDatabase database = new SqlDatabase(databaseName, settings.ConnectionString, new List<BlueprintLanguage> { new BlueprintLanguage(1, string.Empty), new BlueprintLanguage(2, "EN") });
                database.RAMCacheRefreshInterval = TimeSpan.FromHours(1);
                database.RegisterEntity<IncludedProduct>();
                settings.InitialCatalog = databaseName;
                using SqlConnection writer = new SqlConnection(settings.ConnectionString);
                writer.Open();
                CheckIncludes(database, writer);
            }
            finally
            {
                using SqlCommand command = master.CreateCommand();
                command.CommandText = $"IF DB_ID(@name) IS NOT NULL DROP DATABASE [{databaseName}];";
                command.Parameters.AddWithValue("@name", databaseName);
                command.ExecuteNonQuery();
            }
            Console.WriteLine($"RAM cache AutoInclude checks passed: {getAssertionCount()} assertions.");
        }

        /// <summary>Ελέγχει γλώσσες, ανεξάρτητα αντίγραφα, έγκαιρο invalidation και ανανέωση όλων των σχετικών πινάκων.</summary>
        /// <param name="database">Η βάση της συγκεκριμένης δοκιμής.</param>
        /// <param name="writer">Η ανεξάρτητη σύνδεση για εξωτερικές SQL αλλαγές.</param>
        /// <exception cref="Exception">Αποτυγχάνει η εκτέλεση ή κάποια αναμενόμενη συμπεριφορά.</exception>
        private void CheckIncludes(BlueprintDatabase database, SqlConnection writer)
        {
            using (SqlCommand command = writer.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM sys.change_tracking_tables;";
                Assert((int)command.ExecuteScalar()! == 3, "Only the Product, VAT category and Metadata RAM entities must be tracked.");
                command.CommandText = "SELECT COUNT(*) FROM sys.change_tracking_tables WHERE object_id IN (OBJECT_ID(N'dbo.IncludeDetails'), OBJECT_ID(N'dbo.IncludeOptions'));";
                Assert((int)command.ExecuteScalar()! == 0, "DB AutoInclude entities must not be promoted into RAM cache.");
                command.CommandText = "SELECT COUNT(*) FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.IncludeBrands');";
                Assert((int)command.ExecuteScalar()! == 0, "A relationship without AutoInclude must not implicitly become cached.");
            }
            ExecuteSql(writer, """
                INSERT dbo.IncludeVatCategories (ID, Rate, Title, TitleEN) VALUES (1, 24, N'VAT', NULL);
                INSERT dbo.IncludeMetadata (ID, Title, TitleEN) VALUES (1, N'Metadata', N'Metadata EN');
                INSERT dbo.IncludeDetails (ID, MetadataID) VALUES (1, 1);
                INSERT dbo.IncludeBrands (ID, Title) VALUES (1, N'Brand');
                INSERT dbo.IncludeProducts (ID, Title, TitleEN, VatCategoryID, DetailsID, BrandID) VALUES (1, N'Original', N'Original EN', 1, 1, 1), (2, N'Second', N'Second EN', NULL, NULL, 1);
                INSERT dbo.IncludeOptions (ID, ProductID, Title) VALUES (1, 1, N'Red');
                """);
            WaitForRefresh(database, () => IncludedProduct.Where(product => true).Count() == 2);
            IncludedProduct retained = IncludedProduct.Where(product => product.ID == 1, 2).Single();
            Assert(retained.Title == "Original EN" && retained.VatCategory!.Rate == 24m, "Full cached entities must include the already RAMCached VAT category.");
            Assert(retained.VatCategory!.Title == "VAT" && retained.VatCategory.CurrentLanguage == 2, "Included references must preserve multilingual fallback and the requested language.");
            Assert(retained.VatCategory!.Products.Count == 1 && ReferenceEquals(retained.VatCategory.Products[0], retained), "Included references must preserve EF inverse collection fix-up without caching extra rows.");
            Assert(retained.Details!.Metadata.Title == "Metadata EN", "A RAM to DB to RAM path must preserve the selected language.");
            Assert(retained.Options.Count == 1 && ReferenceEquals(retained.Options[0].Product, retained), "Included collections must reuse cyclic ancestors without unbounded recursion.");
            IncludedProduct empty = IncludedProduct.Where(product => product.ID == 2).First();
            Assert(empty.VatCategory == null && empty.Details == null && empty.Options.Count == 0, "Missing optional references and empty collections must remain valid.");
            Assert(IncludedProduct.Where(product => true).OrderBy(product => product.ID).Take(1).ToArray()[0].VatCategory!.Rate == 24m, "Paged entity queries must populate AutoIncludes from RAM.");
            Assert(IncludedProduct.Where(product => product.ID == 1).Select(product => product).ToList()[0].Options.Count == 1, "General cached materialization must include relationships too.");
            Assert(IncludedVatCategory.Where(category => true).Single().Rate == 24m, "Explicitly cached related contracts must remain independently queryable.");
            Assert(IncludedMetadata.Where(metadata => true, 2).Single().Title == "Metadata EN", "Implicit cached dependencies must also serve direct entity queries.");

            IncludedProduct edited = IncludedProduct.Where(product => product.ID == 1, 2).Single();
            edited.VatCategory!.Rate = 99m;
            edited.Details!.Metadata.Title = "Private edit";
            edited.Options[0].Title = "Private option";
            edited.Options.Clear();
            IncludedProduct unchanged = IncludedProduct.Where(product => product.ID == 1, 2).Single();
            Assert(unchanged.VatCategory!.Rate == 24m && unchanged.Details!.Metadata.Title == "Metadata EN" && unchanged.Options[0].Title == "Red", "Editing related values and collections must not mutate the cache.");

            ExecuteSql(writer, """
                BEGIN TRANSACTION;
                UPDATE dbo.IncludeProducts SET Title = N'Updated', TitleEN = N'Updated EN' WHERE ID = 1;
                UPDATE dbo.IncludeVatCategories SET Rate = 13 WHERE ID = 1;
                UPDATE dbo.IncludeMetadata SET TitleEN = N'Updated metadata EN' WHERE ID = 1;
                UPDATE dbo.IncludeOptions SET Title = N'Blue' WHERE ID = 1;
                UPDATE dbo.IncludeBrands SET Title = N'Updated brand' WHERE ID = 1;
                COMMIT;
                """);
            IncludedProduct stale = IncludedProduct.Where(product => product.ID == 1, 2).Single();
            Assert(stale.Title == "Original EN" && stale.VatCategory!.Rate == 24m && stale.Options[0].Title == "Blue", "RAM entities must retain snapshots while DB AutoInclude collections read current SQL values.");
            Assert(IncludedDetails.Where(item => item.ID == 1).Single().Metadata.Title == "Metadata", "A DB root must load its RAM reference using the default cache language.");
            Assert(IncludedDetails.Where(item => item.ID == 1, 2).Single().Metadata.Title == "Metadata EN", "A DB root must read stale RAM translations before cache refresh.");
            Assert(IncludedDetails.Where(item => item.ID == 1, 2).ToList()[0].Metadata.Title == "Metadata EN", "DB list materialization must also load the RAM target from its own snapshot.");
            Assert(IncludedOption.Where(item => item.ID == 1, 2).Single().Product.Title == "Original EN", "A DB root must load its RAM parent from the cached snapshot.");
            Assert(IncludedProduct.Where(product => product.VatCategory != null && product.VatCategory.Rate == 24m).Count() == 1, "Filters over an included reference must use its cached value.");
            Assert(IncludedProduct.Where(product => product.ID == 1).Select(product => product.VatCategory!.Rate).Single() == 24m, "Scalar navigation projections must use RAM before refresh.");
            Assert(IncludedProduct.Where(product => product.ID == 1).Select(product => product.VatCategory).ToList()[0]!.Rate == 24m, "Entity navigation projections must return independent cached objects.");
            Assert(IncludedProduct.Where(product => product.ID == 1).OrderBy(product => product.VatCategory!.Rate).ToList()[0].Title == "Original", "Sorting by an included reference must not force SQL.");
            Assert(IncludedProduct.Where(product => product.Options.Any(option => option.Title == "Blue")).Any(), "RAM query collection filters must use the DB child executor's current rows.");
            Assert(IncludedProduct.Where(product => product.ID == 1, 2).Select(product => product.Details!.Metadata.Title).ToList()[0] == "Metadata EN", "Nested navigation projections must preserve cached translations.");
            Assert(IncludedProduct.Where(product => product.Brand.Title == "Updated brand").Select(product => product.Title).First() == "Updated", "Queries using a non-included relationship must retain SQL fallback.");
            Assert(IncludedMetadata.Where(metadata => true, 2).Single().Title == "Metadata EN", "Direct queries of implicit dependencies must also stay cached before refresh.");
            WaitForRefresh(database, () => IncludedProduct.Where(product => product.ID == 1, 2).Single().Title == "Updated EN");
            IncludedProduct refreshed = IncludedProduct.Where(product => product.ID == 1, 2).Single();
            Assert(refreshed.VatCategory!.Rate == 13m && refreshed.Details!.Metadata.Title == "Updated metadata EN" && refreshed.Options[0].Title == "Blue", "All included snapshots must refresh together after external changes.");
            Assert(retained.Title == "Original EN" && retained.VatCategory!.Rate == 24m && retained.Options[0].Title == "Red", "Refresh must not mutate previously returned graphs.");

            ExecuteSql(writer, "INSERT dbo.IncludeOptions (ID, ProductID, Title) VALUES (2, 1, N'Green');");
            Assert(IncludedProduct.Where(product => product.ID == 1).Single().Options.Count == 2, "DB child inserts must be visible without a RAM refresh.");
            ExecuteSql(writer, "UPDATE dbo.IncludeOptions SET ProductID = 2 WHERE ID = 2;");
            Assert(IncludedProduct.Where(product => product.ID == 2).Single().Options.Count == 1, "DB child foreign-key changes must be visible without refreshing the parent cache.");
            Assert(IncludedProduct.Where(product => product.ID == 1).Single().Options.Count == 1, "Refreshing a foreign key must remove the row from the previous principal's index.");
            ExecuteSql(writer, "DELETE dbo.IncludeOptions WHERE ID = 2;");
            Assert(IncludedProduct.Where(product => product.ID == 2).Single().Options.Count == 0, "DB child deletes must be visible without a RAM refresh.");

            IncludedVatCategory category = IncludedVatCategory.Where(item => item.ID == 1).Single();
            category.Rate = 19m;
            IncludedVatCategory.Update(category);
            Assert(IncludedProduct.Where(product => product.ID == 1).Single().VatCategory!.Rate == 19m, "Blueprint writes to an included entity must invalidate the root graph immediately.");

            ExecuteSql(writer, "UPDATE dbo.IncludeVatCategories SET Rate = 7 WHERE ID = 1;");
            Assert(IncludedProduct.Where(product => product.ID == 1).Single().VatCategory!.Rate == 19m, "The long interval must still apply after a Blueprint write.");
            var transaction = database.TransactionFactory.CreateNewTransaction(pending =>
            {
                Assert(IncludedProduct.Where(product => product.ID == 1).Select(product => product.VatCategory!.Rate).Single() == 7m, "Transactions must bypass cached graphs and read current SQL values.");
                Assert(IncludedProduct.SingleOrDefault(product => product.ID == 1)!.VatCategory!.Rate == 7m, "Transaction AutoIncludes must use SQL even when their target normally uses RAM.");
            });
            database.TransactionFactory.ExecuteTransactionAsync(transaction).GetAwaiter().GetResult();
            Assert(IncludedProduct.Where(product => product.ID == 1).Single().VatCategory!.Rate == 7m, "After a transaction the cache must synchronize the committed related value.");
            ExecuteSql(writer, "INSERT dbo.IncludeProducts (ID, Title, TitleEN, BrandID) VALUES (3, N'New', N'New EN', 1); INSERT dbo.IncludeOptions (ID, ProductID, Title) VALUES (3, 3, N'New option');");
            Assert(IncludedOption.SingleOrDefault(option => option.ID == 3)!.Product == null, "A DB reference missing from the RAM snapshot must not silently fall back to SQL.");
            WaitForRefresh(database, () => IncludedOption.SingleOrDefault(option => option.ID == 3)!.Product?.ID == 3);
        }

        /// <summary>Περιμένει πραγματικό refresh με μικρό διάστημα και ελέγχει κατόπιν με μεγάλο διάστημα χωρίς query-triggered refresh.</summary>
        /// <param name="database">Η βάση που διαχειρίζεται τον timer.</param>
        /// <param name="condition">Η αναμενόμενη κατάσταση μετά τον συγχρονισμό.</param>
        /// <exception cref="InvalidOperationException">Δεν ολοκληρώνεται ο συγχρονισμός μέσα σε δέκα δευτερόλεπτα.</exception>
        private void WaitForRefresh(BlueprintDatabase database, Func<bool> condition)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            try
            {
                while (timeout.ElapsedMilliseconds < 10000)
                {
                    database.RAMCacheRefreshInterval = TimeSpan.FromMilliseconds(30);
                    Thread.Sleep(100);
                    database.RAMCacheRefreshInterval = TimeSpan.FromHours(1);
                    if (condition())
                    {
                        Assert(true, "Background refresh completed.");
                        return;
                    }
                }
                throw new InvalidOperationException("AutoInclude snapshots did not refresh within the timeout.");
            }
            finally
            {
                database.RAMCacheRefreshInterval = TimeSpan.FromHours(1);
            }
        }

        /// <summary>Εκτελεί εξωτερικές αλλαγές αποκλειστικά στην προσωρινή βάση της δοκιμής.</summary>
        /// <param name="writer">Η σύνδεση της προσωρινής βάσης.</param>
        /// <param name="sql">Η εντολή που παρακάμπτει το Entity API.</param>
        /// <exception cref="SqlException">Αποτυγχάνει η εντολή SQL.</exception>
        private static void ExecuteSql(SqlConnection writer, string sql)
        {
            using SqlCommand command = writer.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Προσομοιώνει το Product με cached VAT reference, nested includes, συλλογή και μη cached σχέση.</summary>
    [Entity("IncludeProducts")]
    public abstract class IncludedProduct : BlueprintEntityRAM
    {
        [Multilingual]
        public abstract string? Title { get; set; }
        public abstract long? VatCategoryID { get; set; }
        [ForeignKey(nameof(VatCategoryID)), AutoInclude]
        public abstract IncludedVatCategory? VatCategory { get; set; }
        public abstract long? DetailsID { get; set; }
        [ForeignKey(nameof(DetailsID)), AutoInclude]
        public abstract IncludedDetails? Details { get; set; }
        [AutoInclude]
        public abstract List<IncludedOption> Options { get; set; }
        public abstract long BrandID { get; set; }
        [ForeignKey(nameof(BrandID))]
        public abstract IncludedBrand Brand { get; set; }
    }

    /// <summary>Κληρονομεί ήδη από BlueprintEntityRAM για άμεση επιλογή caching.</summary>
    [Entity("IncludeVatCategories")]
    public abstract class IncludedVatCategory : BlueprintEntityRAM
    {
        [Precision(5, 2)]
        public abstract decimal Rate { get; set; }
        public abstract List<IncludedProduct> Products { get; set; }
        [Multilingual, FallbackToDefaultLanguage]
        public abstract string? Title { get; set; }
    }

    /// <summary>Διαβάζεται από SQL και οδηγεί σε δεύτερο επίπεδο σχέσης με RAM entity.</summary>
    [Entity("IncludeDetails")]
    public abstract class IncludedDetails : BlueprintEntityDB
    {
        public abstract long MetadataID { get; set; }
        [ForeignKey(nameof(MetadataID)), AutoInclude]
        public abstract IncludedMetadata Metadata { get; set; }
    }

    /// <summary>Ελέγχει πολυγλωσσική ανάγνωση από RAM ακόμη και μέσω DB parent.</summary>
    [Entity("IncludeMetadata")]
    public abstract class IncludedMetadata : BlueprintEntityRAM
    {
        [Multilingual]
        public abstract string? Title { get; set; }
    }

    /// <summary>Ελέγχει συλλογές και AutoInclude που επιστρέφει κυκλικά στον γονέα.</summary>
    [Entity("IncludeOptions")]
    public abstract class IncludedOption : BlueprintEntityDB
    {
        public abstract long ProductID { get; set; }
        [ForeignKey(nameof(ProductID)), AutoInclude]
        public abstract IncludedProduct Product { get; set; }
        public abstract string? Title { get; set; }
    }

    /// <summary>Παραμένει εκτός RAM cache επειδή η σχέση δεν δηλώνει AutoInclude.</summary>
    [Entity("IncludeBrands")]
    public abstract class IncludedBrand : BlueprintEntityDB
    {
        public abstract string? Title { get; set; }
    }
}
