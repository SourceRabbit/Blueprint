using Blueprint.Database;
using Blueprint.Database.Sql;
using Blueprint.Database.TransactionFactory;
using Blueprint.Entities;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Reflection;
using Product = Blueprint.Tests.Tutorial1.Product;

namespace Blueprint.Tests.Database
{
    /// <summary>Μετρά πραγματικές εισαγωγές Product μέσω Entity API με συγκεκριμένο μέγεθος pool.</summary>
    internal sealed class ContextPoolInsertTest : BlueprintTest
    {
        private readonly int fExpectedPoolSize;
        private readonly string? fServer;
        private readonly int fProductCount;

        /// <summary>Ορίζει το μέγεθος pool, τον server και το πλήθος εγγραφών της δοκιμής.</summary>
        /// <param name="expectedPoolSize">Το μέγεθος που πρέπει να έχει μεταγλωττιστεί στη βιβλιοθήκη.</param>
        /// <param name="server">Προαιρετικό όνομα SQL Server χωρίς αλλαγή της κοινής ρύθμισης.</param>
        /// <param name="productCount">Ο αριθμός των μεμονωμένων εισαγωγών που θα μετρηθούν.</param>
        /// <exception cref="ArgumentOutOfRangeException">Το πλήθος προϊόντων δεν είναι θετικό.</exception>
        public ContextPoolInsertTest(int expectedPoolSize, string? server = null, int productCount = 1)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productCount);
            fExpectedPoolSize = expectedPoolSize;
            fServer = server;
            fProductCount = productCount;
        }

        /// <summary>Μοιράζει μία λίστα Products σε τόσα threads όσες οι θέσεις του pool, μετρά τις εισαγωγές και επαληθεύει τις εγγραφές πριν διαγράψει την προσωρινή βάση.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος, λειτουργία SQL ή καθαρισμός της προσωρινής βάσης.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            int poolSize = (int)typeof(BlueprintDatabase).GetField("CONTEXT_POOL_SIZE", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
            Assert(poolSize == fExpectedPoolSize, "The compiled pool size must match the requested test size.");
            string databaseName = "BlueprintPoolInsert_" + poolSize + "_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(fDatabaseConnectionString) { InitialCatalog = "master", Pooling = true, ConnectTimeout = 5 };
            if (fServer != null)
            {
                settings.DataSource = fServer;
            }
            using (SqlConnection observer = new SqlConnection(settings.ConnectionString))
            {
                observer.Open();
                try
                {
                    using (SqlDatabase database = new SqlDatabase(databaseName, settings.ConnectionString, new List<BlueprintLanguage> { new BlueprintLanguage(1, string.Empty) }))
                    {
                        database.RAMCacheRefreshInterval = TimeSpan.FromHours(1);
                        database.RegisterEntity<Product>();
                        List<BlueprintDatabaseContext> reserved = new List<BlueprintDatabaseContext>();
                        try
                        {
                            for (int index = 0; index < poolSize; index++)
                            {
                                reserved.Add(database.GetContext());
                            }
                            Assert(reserved.Distinct().Count() == poolSize, "Every pool slot must contain a different context.");
                        }
                        finally
                        {
                            foreach (BlueprintDatabaseContext context in reserved)
                            {
                                context.Dispose();
                            }
                        }
                        Product warmup = CreateProduct("WARMUP");
                        Product.Insert(warmup);
                        Product.Delete(item => item.ID == warmup.ID);
                        List<Product> products = new List<Product>(fProductCount);
                        for (int index = 0; index < fProductCount; index++)
                        {
                            products.Add(CreateProduct("POOL-" + poolSize + "-" + index));
                        }
                        Thread[] workers = new Thread[poolSize];
                        int[] affectedRowsPerThread = new int[poolSize];
                        int[] threadIDs = new int[poolSize];
                        Exception?[] failures = new Exception?[poolSize];
                        Stopwatch stopwatch = new Stopwatch();
                        using (CountdownEvent ready = new CountdownEvent(poolSize))
                        using (ManualResetEventSlim start = new ManualResetEventSlim())
                        {
                            for (int index = 0; index < workers.Length; index++)
                            {
                                int workerIndex = index;
                                workers[index] = new Thread(() =>
                                {
                                    threadIDs[workerIndex] = Environment.CurrentManagedThreadId;
                                    ready.Signal();
                                    start.Wait();
                                    try
                                    {
                                        for (int productIndex = workerIndex; productIndex < products.Count; productIndex += poolSize)
                                        {
                                            affectedRowsPerThread[workerIndex] += Product.Insert(products[productIndex]);
                                        }
                                    }
                                    catch (Exception exception)
                                    {
                                        failures[workerIndex] = exception;
                                    }
                                });
                                workers[index].Start();
                            }
                            try
                            {
                                Assert(ready.Wait(TimeSpan.FromSeconds(10)), "Every worker thread must be ready before timing starts.");
                                stopwatch.Start();
                                start.Set();
                                foreach (Thread worker in workers)
                                {
                                    worker.Join();
                                }
                                stopwatch.Stop();
                            }
                            finally
                            {
                                start.Set();
                                foreach (Thread worker in workers)
                                {
                                    worker.Join();
                                }
                            }
                        }
                        if (failures.Any(exception => exception != null))
                        {
                            throw new AggregateException(failures.OfType<Exception>());
                        }
                        Assert(threadIDs.Distinct().Count() == poolSize, "The number of distinct worker threads must match the pool size.");
                        for (int index = 0; index < poolSize; index++)
                        {
                            int expectedRows = fProductCount / poolSize + (index < fProductCount % poolSize ? 1 : 0);
                            Assert(affectedRowsPerThread[index] == expectedRows, "Each thread must insert its complete, non-overlapping share of the product list.");
                            Console.WriteLine($"Pool {poolSize}: thread {threadIDs[index]} inserted {affectedRowsPerThread[index]} products.");
                        }
                        int affectedRows = affectedRowsPerThread.Sum();
                        Assert(affectedRows == fProductCount && products.All(product => product.ID > 0 && product.Version.Length == 8), "Every Entity Insert must persist one row and populate its ID and rowversion.");
                        Assert(products.Select(product => product.ID).Distinct().Count() == fProductCount, "Every inserted product must have a distinct ID.");
                        using (SqlCommand command = observer.CreateCommand())
                        {
                            command.CommandText = $"SELECT COUNT(*), COUNT(DISTINCT SKU), SUM(Stock), MIN(Price), MAX(Price) FROM [{databaseName}].dbo.Products;";
                            using (SqlDataReader reader = command.ExecuteReader())
                            {
                                Assert(reader.Read() && reader.GetInt32(0) == fProductCount && reader.GetInt32(1) == fProductCount, "An independent SQL connection must see every committed product exactly once.");
                                Assert(reader.GetInt32(2) == fProductCount * 10 && reader.GetDecimal(3) == 19.95m && reader.GetDecimal(4) == 19.95m, "Stored stocks and prices must match the inserted products.");
                            }
                        }
                        List<Product> stored = Product.Where(item => item.ID > 0).OrderBy(item => item.ID).ToList();
                        Assert(stored.Count == fProductCount && stored.Zip(products.OrderBy(product => product.ID)).All(pair => pair.First.ID == pair.Second.ID && pair.First.SKU == pair.Second.SKU && pair.First.Title == pair.Second.Title && pair.First.Price == pair.Second.Price), "Entity queries must read every inserted product correctly through the refreshed RAM cache.");
                        Console.WriteLine(FormattableString.Invariant($"RESULT pool={poolSize} threads={poolSize} products={fProductCount} insert_ms={stopwatch.Elapsed.TotalMilliseconds:F3} inserts_per_second={fProductCount / stopwatch.Elapsed.TotalSeconds:F2} sql_verified=true entity_verified=true"));
                        CheckConcurrentTransactions(database);
                    }
                }
                finally
                {
                    using (SqlCommand command = observer.CreateCommand())
                    {
                        command.CommandText = $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END;";
                        command.Parameters.AddWithValue("@name", databaseName);
                        command.ExecuteNonQuery();
                    }
                }
            }
            Console.WriteLine($"Pool {poolSize} insertion checks passed: {getAssertionCount()} assertions; temporary database removed.");
        }

        /// <summary>Κρατά ένα transaction ανοιχτό ενώ δεύτερο ολοκληρώνεται, ελέγχοντας nested queries, rollback και επιστροφή των contexts.</summary>
        /// <param name="database">Η ενεργή βάση της δοκιμής.</param>
        /// <exception cref="Exception">Αποτυγχάνει η παράλληλη εκτέλεση ή η απομόνωση των transactions.</exception>
        private void CheckConcurrentTransactions(BlueprintDatabase database)
        {
            Product rolledBack = CreateProduct("ROLLBACK");
            Product committed = CreateProduct("COMMIT");
            bool rollbackReadOwnWrite = false;
            bool commitReadOwnWrite = false;
            bool nestedSubmissionRejected = false;
            bool disposalRejected = false;
            bool rawCommandRejected = false;
            using (ManualResetEventSlim firstEntered = new ManualResetEventSlim())
            using (ManualResetEventSlim releaseFirst = new ManualResetEventSlim())
            {
                DatabaseTransaction first = database.TransactionFactory.CreateNewTransaction(pending =>
                {
                    pending.AddInsert(rolledBack);
                    pending.AddValidation(() =>
                    {
                        rollbackReadOwnWrite = Product.SingleOrDefault(item => item.ID == rolledBack.ID)?.SKU == "ROLLBACK";
                        try
                        {
                            database.TransactionFactory.ExecuteTransactionAsync(new DatabaseTransaction());
                        }
                        catch (InvalidOperationException)
                        {
                            nestedSubmissionRejected = true;
                        }
                        try
                        {
                            database.Dispose();
                        }
                        catch (InvalidOperationException)
                        {
                            disposalRejected = true;
                        }
                        try
                        {
                            database.ExecuteNonQuery("SELECT 1;");
                        }
                        catch (InvalidOperationException)
                        {
                            rawCommandRejected = true;
                        }
                        firstEntered.Set();
                        if (!releaseFirst.Wait(TimeSpan.FromSeconds(15)))
                        {
                            throw new TimeoutException("The concurrent transaction test did not release the first worker.");
                        }
                        throw new InvalidOperationException("Expected concurrent rollback.");
                    });
                });
                DatabaseTransaction second = database.TransactionFactory.CreateNewTransaction(pending =>
                {
                    pending.AddInsert(committed);
                    pending.AddValidation(() => commitReadOwnWrite = Product.SingleOrDefault(item => item.ID == committed.ID)?.SKU == "COMMIT");
                });
                Task<int> firstExecution = database.TransactionFactory.ExecuteTransactionAsync(first);
                Task<int>? secondExecution = null;
                try
                {
                    Assert(firstEntered.Wait(TimeSpan.FromSeconds(10)), "The first transaction must reach validation with its write still uncommitted.");
                    secondExecution = database.TransactionFactory.ExecuteTransactionAsync(second);
                    Assert(secondExecution.Wait(TimeSpan.FromSeconds(10)), "A second transaction must commit while the first transaction is still open.");
                    Assert(secondExecution.Result == 1 && !firstExecution.IsCompleted, "Transaction execution must not be serialized by a database operation lock.");
                }
                finally
                {
                    releaseFirst.Set();
                    try
                    {
                        firstExecution.GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException exception) when (exception.Message == "Expected concurrent rollback.")
                    {
                    }
                    secondExecution?.GetAwaiter().GetResult();
                }
                Assert(first.IsCompleted && !first.IsCommitted && rolledBack.ID == 0, "Rollback must restore generated IDs and release its context.");
                Assert(second.IsCompleted && second.IsCommitted && committed.ID > 0, "The other transaction must keep its independent commit.");
                Assert(rollbackReadOwnWrite && commitReadOwnWrite, "Each nested Entity query must read its own transaction's uncommitted writes.");
                Assert(nestedSubmissionRejected && disposalRejected && rawCommandRejected, "Transaction callbacks must retain nested-submission, disposal, and standalone-SQL guards.");
                Assert(!Product.Where(item => item.SKU == "ROLLBACK").Any() && Product.Where(item => item.SKU == "COMMIT").Count() == 1, "The refreshed cache must contain only committed transaction writes.");
                Assert(Product.Delete(item => item.ID == committed.ID) == 1, "An Entity operation after both transactions must reuse a released context successfully.");
                Console.WriteLine("Concurrent transactions passed: overlapping SQL execution, own-write reads, commit, rollback, callback guards, and context reuse.");
            }
        }

        /// <summary>Δημιουργεί ένα Product με σταθερά δεδομένα ώστε οι δύο μετρήσεις να έχουν ίδιο φορτίο.</summary>
        /// <param name="sku">Το μοναδικό SKU του προϊόντος.</param>
        /// <returns>Το νέο, μη αποθηκευμένο Product.</returns>
        private static Product CreateProduct(string sku)
        {
            Product product = Product.Create();
            product.SKU = sku;
            product.Title = "Pool insert test";
            product.ShortDescription = "Product inserted through the Entity API.";
            product.Description = "Temporary integration test product.";
            product.Price = 19.95m;
            product.Stock = 10;
            return product;
        }
    }
}
