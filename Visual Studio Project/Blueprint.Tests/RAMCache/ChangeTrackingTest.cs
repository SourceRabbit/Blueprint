using Blueprint.Database;
using Blueprint.Entities;
using Blueprint.Database.Sql;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using System.Diagnostics;

namespace Blueprint.Tests.RAMCache
{
    /// <summary>Επαληθεύει το SQL Server Change Tracking και την αυτόματη ενημέρωση της RAM cache από ανεξάρτητη σύνδεση.</summary>
    internal sealed class ChangeTrackingTest : BlueprintTest
    {
        private const int REFRESH_INTERVAL_MS = 100;
        private const int WAIT_TIMEOUT_MS = 10000;
        private static readonly TimeSpan fLongRefreshInterval = TimeSpan.FromHours(1);

        /// <summary>Δημιουργεί απομονωμένη βάση, εκτελεί τους ελέγχους και αποδεσμεύει όλα τα προσωρινά δεδομένα.</summary>
        /// <exception cref="Exception">Αποτυγχάνει η επικοινωνία με SQL, κάποιος έλεγχος ή η διαγραφή της προσωρινής βάσης.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            string databaseName = "BlueprintChangeTracking_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(fDatabaseConnectionString)
            {
                InitialCatalog = "master",
                ConnectTimeout = 5,
                Pooling = false
            };
            using (SqlConnection master = new SqlConnection(settings.ConnectionString))
            {
                master.Open();
                try
                {
                    List<BlueprintLanguage> languages = new List<BlueprintLanguage> { new BlueprintLanguage(1, string.Empty), new BlueprintLanguage(2, "EN") };
                    using (BlueprintDatabase database = new SqlDatabase(databaseName, settings.ConnectionString, languages))
                    {
                        database.RAMCacheRefreshInterval = fLongRefreshInterval;
                        database.RegisterEntity<CachedRecord>();
                        settings.InitialCatalog = databaseName;
                        using (SqlConnection writer = new SqlConnection(settings.ConnectionString))
                        {
                            writer.Open();
                            CheckChanges(database, writer);
                        }
                    }
                }
                finally
                {
                    using (SqlCommand command = master.CreateCommand())
                    {
                        command.CommandText = $"IF DB_ID(@databaseName) IS NOT NULL DROP DATABASE [{databaseName}];";
                        command.Parameters.AddWithValue("@databaseName", databaseName);
                        command.ExecuteNonQuery();
                    }
                }
            }
            Console.WriteLine($"RAM cache Change Tracking checks passed: {getAssertionCount()} assertions (refresh interval: {REFRESH_INTERVAL_MS} ms).");
        }

        /// <summary>Ελέγχει εξωτερικά updates, inserts, deletes, rollback, ανεξάρτητα αντίγραφα και invalidation από το Entity API.</summary>
        /// <param name="database">Η ενεργή προσωρινή βάση.</param>
        /// <param name="writer">Η ανεξάρτητη σύνδεση που παρακάμπτει το Blueprint.</param>
        /// <exception cref="Exception">Αποτυγχάνει SQL εντολή ή η cache δεν εμφανίζει τη σωστή κατάσταση.</exception>
        private void CheckChanges(BlueprintDatabase database, SqlConnection writer)
        {
            using (SqlCommand command = writer.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM sys.change_tracking_databases WHERE database_id = DB_ID();";
                Assert((int)command.ExecuteScalar()! == 1, "Registration must enable database Change Tracking.");
                command.CommandText = "SELECT COUNT(*) FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.RAMCachedRecords');";
                Assert((int)command.ExecuteScalar()! == 1, "Registration must enable Change Tracking for the cached table.");
            }

            IQueryable<CachedRecord> query = CachedRecord.Where(item => true);
            Assert(query.Count() == 0, "The initial cached table must be empty.");
            CachedRecord updated = CachedRecord.Create();
            updated.Price = 10m;
            updated.Title = "Original";
            updated.CurrentLanguage = 2;
            updated.Title = "Original EN";
            CachedRecord deleted = CachedRecord.Create();
            deleted.Price = 20m;
            CachedRecord.Insert(new[] { updated, deleted });
            Assert(query.Count() == 2, "Blueprint inserts must invalidate the cache for the next read.");
            CachedRecord retained = query.Single(item => item.ID == updated.ID);

            Assert(ExecuteSql(writer, "UPDATE dbo.RAMCachedRecords SET Price = 15, TitleEN = N'Updated EN' WHERE ID = @id;", new SqlParameter("@id", updated.ID)) == 1, "The external SQL update must affect the intended row.");
            Assert(query.Single(item => item.ID == updated.ID).Price == 10m, "Before refresh, an eligible query must return cached values rather than newer SQL data.");
            Assert(CachedRecord.Where(item => item.ID == updated.ID, 2).Single().Title == "Original EN", "Translations must also remain cached until synchronization.");
            Assert(WaitForBackgroundRefresh(database, () => query.Single(item => item.ID == updated.ID).Price == 15m && CachedRecord.Where(item => item.ID == updated.ID, 2).Single().Title == "Updated EN"), "The background timer must synchronize externally updated values and translations.");
            Assert(retained.Price == 10m && retained.Title == "Original", "Synchronization must not mutate previously returned instances.");
            Console.WriteLine("PASS: external UPDATE and translations synchronized by the background timer.");

            long insertedID;
            using (SqlCommand command = writer.CreateCommand())
            {
                command.CommandText = "INSERT INTO dbo.RAMCachedRecords (Price, Title, TitleEN) OUTPUT INSERTED.ID VALUES (30, N'Inserted', N'Inserted EN');";
                insertedID = (long)command.ExecuteScalar()!;
            }
            Assert(!query.Any(item => item.ID == insertedID), "An external insert must not appear before the next synchronization.");
            Assert(WaitForBackgroundRefresh(database, () => query.Count() == 3 && query.Any(item => item.ID == insertedID && item.Price == 30m)), "The background timer must add externally inserted rows to the existing query's cache.");
            Console.WriteLine("PASS: external INSERT synchronized by the background timer.");

            Assert(ExecuteSql(writer, "DELETE FROM dbo.RAMCachedRecords WHERE ID = @id;", new SqlParameter("@id", deleted.ID)) == 1, "The external SQL delete must affect the intended row.");
            Assert(query.Any(item => item.ID == deleted.ID), "An externally deleted row must remain in the snapshot until synchronization.");
            Assert(WaitForBackgroundRefresh(database, () => query.Count() == 2 && !query.Any(item => item.ID == deleted.ID)), "The background timer must remove externally deleted rows from the cache.");
            Console.WriteLine("PASS: external DELETE synchronized by the background timer.");

            using (SqlTransaction transaction = writer.BeginTransaction())
            {
                using (SqlCommand command = writer.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE dbo.RAMCachedRecords SET Price = 999 WHERE ID = @id;";
                    command.Parameters.AddWithValue("@id", updated.ID);
                    Assert(command.ExecuteNonQuery() == 1, "The uncommitted update must affect the intended row.");
                }
                Assert(WaitForBackgroundRefresh(database, () => query.Single(item => item.ID == updated.ID).Price == 15m), "Uncommitted external changes must not become cached values.");
                transaction.Rollback();
            }
            Assert(WaitForBackgroundRefresh(database, () => query.Single(item => item.ID == updated.ID).Price == 15m), "Rolled-back SQL changes must not become cached values.");
            Console.WriteLine("PASS: uncommitted and rolled-back changes stay outside the cache.");

            CachedRecord copy = query.Single(item => item.ID == insertedID);
            copy.Price = 40m;
            Assert(query.Single(item => item.ID == insertedID).Price == 30m, "Editing a returned object must not change shared cached data.");
            Assert(CachedRecord.Update(copy) == 1, "The Entity API update must persist the edited copy.");
            Assert(query.Single(item => item.ID == insertedID).Price == 40m, "Blueprint updates must invalidate the cache even when the refresh interval is one hour.");
            Console.WriteLine("PASS: independent copies and immediate invalidation after Blueprint writes.");
        }

        /// <summary>Δίνει χρόνο στον timer και ελέγχει τη RAM με μεγάλο interval, ώστε η ανάγνωση να μην προκαλεί refresh λόγω ηλικίας.</summary>
        /// <param name="database">Η βάση που διαχειρίζεται τον timer της cache.</param>
        /// <param name="condition">Ο έλεγχος της αναμενόμενης κατάστασης της RAM.</param>
        /// <returns>True αν ο συγχρονισμός ολοκληρωθεί μέσα στο χρονικό όριο.</returns>
        /// <exception cref="Exception">Αποτυγχάνει ανάγνωση της cache.</exception>
        private static bool WaitForBackgroundRefresh(BlueprintDatabase database, Func<bool> condition)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            try
            {
                while (timeout.ElapsedMilliseconds < WAIT_TIMEOUT_MS)
                {
                    database.RAMCacheRefreshInterval = TimeSpan.FromMilliseconds(REFRESH_INTERVAL_MS);
                    Thread.Sleep(REFRESH_INTERVAL_MS * 3);
                    database.RAMCacheRefreshInterval = fLongRefreshInterval;
                    if (condition())
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                database.RAMCacheRefreshInterval = fLongRefreshInterval;
            }
        }

        /// <summary>Εκτελεί εξωτερική SQL αλλαγή με παραμέτρους, χωρίς invalidation μέσω Blueprint.</summary>
        /// <param name="connection">Η ανεξάρτητη σύνδεση SQL.</param>
        /// <param name="sql">Η εντολή αλλαγής των προσωρινών δεδομένων.</param>
        /// <param name="parameters">Οι παράμετροι της εντολής.</param>
        /// <returns>Το πλήθος των επηρεασμένων εγγραφών.</returns>
        /// <exception cref="SqlException">Αποτυγχάνει η SQL εντολή.</exception>
        private static int ExecuteSql(SqlConnection connection, string sql, params SqlParameter[] parameters)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.Parameters.AddRange(parameters);
                return command.ExecuteNonQuery();
            }
        }
    }
}
