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
            CheckPageArguments(updated.ID, deleted.ID);
            CheckPrimaryKeyLookup(updated.ID, deleted.ID);
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

            const int CHANGED_RECORD_COUNT = 2105;
            ExecuteSql(writer, """
                WITH numbers AS (
                    SELECT 1 AS n UNION ALL SELECT n + 1 FROM numbers WHERE n < @count
                )
                INSERT dbo.RAMCachedRecords (Price) SELECT 23 FROM numbers OPTION (MAXRECURSION 0);
                """, new SqlParameter("@count", CHANGED_RECORD_COUNT));
            Assert(WaitForBackgroundRefresh(database, () => query.Count(item => item.Price == 23m) == CHANGED_RECORD_COUNT), "One refresh must load every changed ID even when the delta exceeds the SQL parameter limit.");
            Assert(query.Count() == CHANGED_RECORD_COUNT + 2, "A multi-batch refresh must retain unchanged rows.");
        }

        /// <summary>Checks primary-key filtering with the public Entity API, including queries that must keep their full source.</summary>
        /// <param name="firstID">The ID of the record priced at ten.</param>
        /// <param name="secondID">The ID of the record priced at twenty.</param>
        /// <exception cref="Exception">A query returned an incorrect row, count, projection or order.</exception>
        private void CheckPrimaryKeyLookup(long firstID, long secondID)
        {
            long id = firstID;
            IQueryable<CachedRecord> filtered = CachedRecord.Where(record => record.ID == id && record.Price >= 20m);
            Assert(filtered.SingleOrDefault() == null, "The remaining filter must reject a matching primary key.");
            id = secondID;
            Assert(filtered.Select(record => record.Price).Single() == 20m && filtered.Count() == 1 && filtered.Any(), "The same query must use its current ID for scalar operations.");
            Assert(filtered.Select(record => record.Price).ToArray().SequenceEqual(new[] { 20m }), "A projection must use the same candidate as an entity query.");
            Assert(CachedRecord.Where(record => record.Price >= 20m && id == record.ID).ToList().Single().ID == id, "Either operand order and a preceding scalar filter must select the correct row.");
            Assert(CachedRecord.Where(record => record.Price >= 20m).Where(record => record.ID == id).LongCount() == 1L, "Consecutive Where operations must keep both predicates.");
            Assert(!filtered.Skip(1).Take(1).Any(), "Paging after the lookup must apply to the filtered sequence.");
            long lastID = Math.Max(firstID, secondID);
            Assert(!CachedRecord.Where(record => true).OrderBy(record => record.ID).Take(1).Where(record => record.ID == lastID).Any(), "A lookup must not move across preceding pagination.");
            Assert(CachedRecord.Where(record => record.ID == firstID || record.ID == secondID).Count() == 2, "Disjunction must retain both candidates.");
            id = -1;
            Assert(!filtered.Any() && filtered.Count() == 0 && filtered.SingleOrDefault() == null, "A missing key must yield an empty sequence.");
            AssertThrows<InvalidOperationException>(() => filtered.Single(), "Single must throw for a missing primary key.");

            long[] sourceOrder = CachedRecord.Where(record => true).Select(record => record.ID).ToArray();
            long changingID = sourceOrder[0];
            long nextID = sourceOrder[1];
            Func<bool> advance = () =>
            {
                changingID = nextID;
                return true;
            };
            long[] moving = CachedRecord.Where(record => record.ID == changingID && advance()).Select(record => record.ID).ToArray();
            Assert(moving.SequenceEqual(sourceOrder), "A callback that changes the captured ID must preserve enumeration semantics.");
        }

        /// <summary>Checks that repeated page shapes take their arguments from the current execution, including concurrent callers.</summary>
        /// <param name="firstID">The ID of the record priced at ten with both translations populated.</param>
        /// <param name="secondID">The ID of the record priced at twenty.</param>
        private void CheckPageArguments(long firstID, long secondID)
        {
            long[] ids = { firstID };
            decimal minimum = 10m;
            IQueryable<CachedRecord> query = CachedRecord.Where(record => ids.Contains(record.ID) && record.Price >= minimum);
            Assert(query.Single().ID == firstID, "The first execution must bind the original ID.");
            ids = new[] { secondID };
            minimum = 20m;
            Assert(query.Single().ID == secondID, "Re-enumeration must observe the new collection and threshold.");
            minimum = 21m;
            Assert(query.ToArray().Length == 0, "A changed threshold must be applied on a cache hit.");

            for (int index = 0; index < 4; index++)
            {
                int skip = index % 2;
                int take = index == 3 ? 0 : 1;
                long[] actual = CachedRecord.Where(record => true).OrderBy(record => record.ID).Skip(skip).Take(take).ToArray().Select(record => record.ID).ToArray();
                long[] expected = new[] { firstID, secondID }.Order().Skip(skip).Take(take).ToArray();
                Assert(actual.SequenceEqual(expected), "Skip and Take must use the current page bounds.");

                long language = skip + 1;
                string title = skip == 0 ? "Original" : "Original EN";
                Assert(CachedRecord.Where(record => record.Title == title, language).Single().ID == firstID, "The plan must read the mapped column of the selected language.");
            }

            long[] results = new long[32];
            Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 6 }, index =>
            {
                long id = index % 2 == 0 ? firstID : secondID;
                results[index] = CachedRecord.Where(record => record.ID == id).Single().ID;
            });
            Assert(results.Select((id, index) => id == (index % 2 == 0 ? firstID : secondID)).All(matches => matches), "Concurrent selections must use independent arguments.");
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
