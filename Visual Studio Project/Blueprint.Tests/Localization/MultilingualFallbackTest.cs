using Blueprint.Database;
using Blueprint.Database.Queries;
using Blueprint.Database.Entities;
using Blueprint.Database.Entities.Annotations;
using Blueprint.Database.Sql;
using Blueprint.Database.TransactionFactory;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;

namespace Blueprint.Tests.Localization
{
    /// <summary>Ελέγχει το fallback μέσω των δημόσιων APIs σε SQL και RAM cache χωρίς αλλαγή των αποθηκευμένων μεταφράσεων.</summary>
    internal sealed class MultilingualFallbackTest : BlueprintTest
    {
        private const long DEFAULT_LANGUAGE_ID = 7;
        private const long SELECTED_LANGUAGE_ID = 2;

        /// <summary>Δημιουργεί απομονωμένη βάση, ελέγχει το fallback και διαγράφει την προσωρινή βάση.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος ή λειτουργία της προσωρινής βάσης.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            string databaseName = "BlueprintFallback_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(fDatabaseConnectionString)
            {
                InitialCatalog = "master",
                ConnectTimeout = 5,
                Pooling = false
            };
            using (SqlConnection connection = new SqlConnection(settings.ConnectionString))
            {
                connection.Open();
                try
                {
                    List<BlueprintLanguage> languages = new List<BlueprintLanguage> { new BlueprintLanguage(SELECTED_LANGUAGE_ID, "FR"), new BlueprintLanguage(DEFAULT_LANGUAGE_ID, string.Empty) };
                    using (BlueprintDatabase database = new SqlDatabase(databaseName, settings.ConnectionString, languages))
                    {
                        database.RAMCacheRefreshInterval = TimeSpan.FromHours(1);
                        AssertThrows<ArgumentException>(() => database.RegisterEntity<UntranslatedFallback>(), "Fallback must require Multilingual.");
                        AssertThrows<ArgumentException>(() => database.RegisterEntity<NonStringFallback>(), "Fallback must require a string translation.");
                        AssertThrows<ArgumentException>(() => database.RegisterEntity<UnmappedFallback>(), "Fallback must reject unmapped translations.");
                        database.RegisterEntity<SqlFallbackRecord>();
                        database.RegisterEntity<CachedFallbackRecord>();
                        CheckContract<SqlFallbackRecord>(database, connection, databaseName, "FallbackSqlRecords");
                        CheckContract<CachedFallbackRecord>(database, connection, databaseName, "FallbackCachedRecords");
                        CheckRelatedReads(database);
                        CheckExternalCacheChanges(database, connection, databaseName);
                    }
                }
                finally
                {
                    using (SqlCommand command = connection.CreateCommand())
                    {
                        command.CommandText = $"IF DB_ID(@databaseName) IS NOT NULL DROP DATABASE [{databaseName}];";
                        command.Parameters.AddWithValue("@databaseName", databaseName);
                        command.ExecuteNonQuery();
                    }
                }
            }
            Console.WriteLine($"Multilingual fallback checks passed: {getAssertionCount()} assertions.");
        }

        /// <summary>Συγκρίνει entities, projections, φίλτρα, ταξινόμηση, paging και διατήρηση των μεταφράσεων.</summary>
        /// <typeparam name="TEntity">Το cached ή SQL entity με το κοινό contract.</typeparam>
        /// <param name="database">Η ενεργή προσωρινή βάση.</param>
        /// <param name="connection">Η σύνδεση ελέγχου των πραγματικών στηλών.</param>
        /// <param name="databaseName">Το μοναδικό όνομα της προσωρινής βάσης.</param>
        /// <param name="table">Ο πίνακας του ελεγχόμενου contract.</param>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος ή λειτουργία SQL.</exception>
        private void CheckContract<TEntity>(BlueprintDatabase database, SqlConnection connection, string databaseName, string table) where TEntity : BlueprintEntity, FallbackFields
        {
            (string? Default, string? Translation, string? Expected)[] cases =
            {
                ("Zulu", null, "Zulu"),
                ("Alpha", "", "Alpha"),
                ("Default", "Translated", "Translated"),
                ("Space default", " ", " "),
                ("Tab default", "\t", "\t"),
                (null, null, null),
                ("", null, ""),
                (null, "", null),
                ("", "", ""),
                (null, "Available", "Available"),
                ("Other", "Trailing ", "Trailing ")
            };
            for (int index = 0; index < cases.Length; index++)
            {
                TEntity entity = BlueprintEntityQueries.Create<TEntity>();
                Assert(entity.CurrentLanguage == DEFAULT_LANGUAGE_ID, "The default language must come from the empty prefix, not the ID or list order.");
                entity.Code = index;
                entity.Title = cases[index].Default;
                entity.PlainTitle = "Plain default";
                entity.CurrentLanguage = SELECTED_LANGUAGE_ID;
                entity.Title = cases[index].Translation;
                entity.PlainTitle = cases[index].Translation;
                Assert(entity.Title == cases[index].Expected && entity.CurrentLanguage == SELECTED_LANGUAGE_ID, "Generated getters must resolve fallback without changing the language.");
                BlueprintEntityQueries.Insert<TEntity>(entity);
            }

            IQueryable<TEntity> query = BlueprintEntityQueries.Where<TEntity>(item => item.Code >= 0, SELECTED_LANGUAGE_ID);
            List<TEntity> entities = query.OrderBy(item => item.Code).ToList();
            List<string?> titles = query.OrderBy(item => item.Code).Select(item => item.Title).ToList();
            for (int index = 0; index < cases.Length; index++)
            {
                Assert(entities[index].Title == cases[index].Expected && titles[index] == cases[index].Expected, "Materialized entities and projections must return the same resolved value, including whitespace.");
                Assert(entities[index].PlainTitle == cases[index].Translation, "Fields without the annotation must retain their stored null or empty translation.");
                Assert(entities[index].CurrentLanguage == SELECTED_LANGUAGE_ID, "Reading must preserve the requested language.");
            }
            Assert(BlueprintEntityQueries.Where<TEntity>(item => item.Code == 0).Single().Title == "Zulu", "Omitting the language must read the default value.");
            Assert(query.Where(item => item.Title == "Alpha").Count() == 1, "Count predicates must include fallback values.");
            Assert(query.LongCount(item => item.Title == "Zulu") == 1, "LongCount predicates must include fallback values.");
            Assert(query.Any(item => item.Title == "Alpha"), "Any must use the resolved value.");
            Assert(query.Count(item => item.Title == null) == 2, "A null default must remain null when the selected translation is missing.");
            List<TEntity> page = query.Where(item => item.Code <= 2).OrderBy(item => item.Title).ThenBy(item => item.Code).Skip(1).Take(2).ToList();
            Assert(page.Select(item => item.Code).SequenceEqual(new[] { 2, 0 }), "Ordering and optimized paging must use fallback values.");
            Assert(query.Where(item => item.Title == "Alpha").Take(1).Single().Code == 1, "Filtered pages must find empty translations through fallback.");

            IQueryable<TEntity> empty = query.Where(item => item.Code == -1);
            Assert(empty.Count() == 0 && empty.LongCount() == 0 && !empty.Any(), "Zero and false must remain valid terminal results.");
            Assert(empty.FirstOrDefault() == null && empty.SingleOrDefault() == null, "Null results must not be mistaken for an unsupported cache query.");
            Assert(empty.ToList().Count == 0, "An empty list must remain a successful query result.");
            AssertThrows<InvalidOperationException>(() => query.Single(), "Multiple matches must propagate the original Single exception.");
            CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            Assert(query.Count(item => item.Title.Contains("ALPHA", options)) == 1, "Text-search counts must preserve fallback and comparison options in both execution paths.");
            Assert(query.Where(item => item.Title.Contains("ALPHA", options)).ToList().Single().Code == 1, "Text-search lists must preserve fallback in both execution paths.");
            Assert(query.OrderByDescending(item => item.Title.StartsWith("ALPHA", options)).ThenBy(item => item.Code).First().Code == 1, "Prefix ranking must remain available for terminal entity reads.");
            List<TEntity> identityProjection = query.OrderBy(item => item.Code).Select(item => item).ToList();
            identityProjection[0].Marker = 99;
            Assert(query.Single(item => item.Code == 0).Marker == 0, "General cached query execution must preserve independent entity copies.");

            TEntity captured = entities[0];
            captured.CurrentLanguage = DEFAULT_LANGUAGE_ID;
            Assert(query.Count(item => item.Title == captured.Title) == 1, "Captured entities must retain their own selected language.");
            AssertThrows<ArgumentOutOfRangeException>(() => BlueprintEntityQueries.Where<TEntity>(item => true, 999).ToList(), "Fallback must not hide an unregistered language.");
            captured.CurrentLanguage = 999;
            AssertThrows<ArgumentOutOfRangeException>(() => { _ = captured.Title; }, "Generated getters must reject an unregistered language.");
            AssertThrows<ArgumentOutOfRangeException>(() => captured.Title = "Invalid", "Generated setters must reject an unregistered language.");

            TEntity changed = entities[1];
            changed.Marker = 1;
            BlueprintEntityQueries.Update<TEntity>(changed);
            AssertStoredTranslation(connection, databaseName, table, changed.ID, "");
            Assert(BlueprintEntityQueries.SingleOrDefault<TEntity>(item => item.ID == changed.ID, SELECTED_LANGUAGE_ID)!.Title == "Alpha", "Updating another property must not persist the fallback value.");
            changed.Title = "French title";
            BlueprintEntityQueries.Update<TEntity>(changed);
            AssertStoredTranslation(connection, databaseName, table, changed.ID, "French title");
            Assert(BlueprintEntityQueries.SingleOrDefault<TEntity>(item => item.ID == changed.ID, DEFAULT_LANGUAGE_ID)!.Title == "Alpha", "Writing a translation must preserve the default value.");
            changed.Title = null;
            BlueprintEntityQueries.Update<TEntity>(changed);
            AssertStoredTranslation(connection, databaseName, table, changed.ID, null);
            changed.CurrentLanguage = DEFAULT_LANGUAGE_ID;
            changed.Title = "New default";
            BlueprintEntityQueries.Update<TEntity>(changed);
            Assert(query.Single(item => item.Code == 1).Title == "New default", "Fallback must reflect subsequent default-language updates after cache invalidation.");

            DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction(pending =>
            {
                TEntity loaded = BlueprintEntityQueries.Where<TEntity>(item => item.Code == 0 && item.Title == "Zulu", SELECTED_LANGUAGE_ID).Single();
                Assert(loaded.Title == "Zulu", "Transaction reads must resolve fallback through SQL even for a cached entity.");
                loaded.Marker = 2;
                pending.AddUpdate(loaded);
            });
            database.TransactionFactory.ExecuteTransactionAsync(transaction).GetAwaiter().GetResult();
            AssertStoredTranslation(connection, databaseName, table, entities[0].ID, null);

            if (typeof(TEntity) == typeof(CachedFallbackRecord))
            {
                Assert(query.Single(item => item.Code == 1).Title == "New default", "The cached row must be synchronized before the external change.");
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"UPDATE [{databaseName}].dbo.[{table}] SET Title = N'External default' WHERE ID = @id;";
                    command.Parameters.AddWithValue("@id", changed.ID);
                    command.ExecuteNonQuery();
                }
                Assert(query.Where(item => item.Code == 1).Select(item => item.Title).Single() == "New default", "Eligible reads must use RAM until the next refresh.");
                Assert(query.Where(item => item.Title == "External default").Count() == 0, "A zero cached count must not fall through to newer SQL data.");
                Assert(query.Where(item => item.Title == "External default").FirstOrDefault() == null, "A null cached entity must not fall through to newer SQL data.");
                Assert(!query.Any(item => item.Title == "External default"), "A false general cache result must not fall through to SQL.");
                Assert(query.Where(item => item.Title == "External default").ToList().Count == 0, "An empty cached list must not fall through to newer SQL data.");
                Assert(query.Where(item => item.Title == "External default").Select(item => item).ToList().Count == 0, "An empty general cached query must not fall through to SQL.");
                Assert(query.Count(item => EF.Functions.Like(item.Title, "External%")) == 1, "Queries delegated from cache to SQL must use the same fallback rules.");
                entities[2].Marker = 3;
                BlueprintEntityQueries.Update<TEntity>(entities[2]);
                Assert(query.Single(item => item.Code == 1).Title == "External default", "Synchronization must pick up external default changes without storing a translation.");
                AssertStoredTranslation(connection, databaseName, table, changed.ID, null);
            }

            TEntity deleted = BlueprintEntityQueries.Create<TEntity>();
            deleted.Code = 99;
            deleted.Title = "Delete through fallback";
            BlueprintEntityQueries.Insert<TEntity>(deleted);
            // Το predicate delete χρησιμοποιεί την προεπιλεγμένη γλώσσα από το δημόσιο static API.
            Assert(BlueprintEntityQueries.Delete<TEntity>(item => item.Title == "Delete through fallback") == 1, "Default-language predicate deletes must still work.");
        }

        /// <summary>Ελέγχει τον περιοδικό συγχρονισμό εξωτερικών updates, inserts και deletes σε ήδη συντεθειμένο query.</summary>
        /// <param name="database">Η ενεργή προσωρινή βάση.</param>
        /// <param name="connection">Η ανεξάρτητη σύνδεση που αλλάζει τα δεδομένα.</param>
        /// <param name="databaseName">Το μοναδικό όνομα της προσωρινής βάσης.</param>
        /// <exception cref="Exception">Αποτυγχάνει λειτουργία SQL ή οι αλλαγές δεν εμφανίζονται στην cache.</exception>
        private void CheckExternalCacheChanges(BlueprintDatabase database, SqlConnection connection, string databaseName)
        {
            CachedFallbackRecord updated = CachedFallbackRecord.Create();
            updated.Code = 1000;
            updated.Title = "Before external update";
            CachedFallbackRecord deleted = CachedFallbackRecord.Create();
            deleted.Code = 1001;
            deleted.Title = "Before external delete";
            CachedFallbackRecord.Insert(new[] { updated, deleted });
            IQueryable<CachedFallbackRecord> query = CachedFallbackRecord.Where(item => item.Code >= 1000, SELECTED_LANGUAGE_ID);
            CachedFallbackRecord retained = query.Single(item => item.ID == updated.ID);
            Assert(query.Count() == 2, "The initial cached snapshot must contain both new records.");
            long insertedID = Math.Max(updated.ID, deleted.ID) + 1000000;
            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = $"UPDATE [{databaseName}].dbo.FallbackCachedRecords SET Title = N'External update' WHERE ID = @updatedID; DELETE FROM [{databaseName}].dbo.FallbackCachedRecords WHERE ID = @deletedID; INSERT INTO [{databaseName}].dbo.FallbackCachedRecords (ID, Code, Marker, Title) VALUES (@insertedID, 1002, 0, N'External insert');";
                    command.Parameters.AddWithValue("@updatedID", updated.ID);
                    command.Parameters.AddWithValue("@deletedID", deleted.ID);
                    command.Parameters.AddWithValue("@insertedID", insertedID);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            TimeSpan previousInterval = database.RAMCacheRefreshInterval;
            try
            {
                database.RAMCacheRefreshInterval = TimeSpan.FromMilliseconds(50);
                bool synchronized = SpinWait.SpinUntil(() =>
                {
                    List<CachedFallbackRecord> rows = query.ToList();
                    return rows.Count == 2 && rows.Any(item => item.ID == updated.ID && item.Title == "External update") && rows.Any(item => item.ID == insertedID && item.Title == "External insert") && rows.All(item => item.ID != deleted.ID);
                }, TimeSpan.FromSeconds(10));
                Assert(synchronized, "A reused query must observe committed external updates, inserts, and deletes after synchronization without a Blueprint write.");
                Assert(retained.Title == "Before external update", "Refreshing the cache must not modify previously returned instances.");
                CachedFallbackRecord copied = query.Single(item => item.ID == insertedID);
                copied.Title = "Private edit";
                Assert(query.Single(item => item.ID == insertedID).Title == "External insert", "Newly synchronized entities must still be returned as independent copies.");
            }
            finally
            {
                database.RAMCacheRefreshInterval = previousInterval;
            }
        }

        /// <summary>Επαληθεύει ότι το fallback δεν αντικαθιστά την πραγματική μετάφραση στη βάση.</summary>
        /// <param name="connection">Η σύνδεση προς την προσωρινή βάση.</param>
        /// <param name="databaseName">Το μοναδικό όνομα της προσωρινής βάσης.</param>
        /// <param name="table">Ο πίνακας του contract.</param>
        /// <param name="id">Το ID της εγγραφής.</param>
        /// <param name="expected">Η αναμενόμενη αποθηκευμένη μετάφραση.</param>
        /// <exception cref="Exception">Η πραγματική μετάφραση διαφέρει ή η ανάγνωση αποτυγχάνει.</exception>
        private void AssertStoredTranslation(SqlConnection connection, string databaseName, string table, long id, string? expected)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT TitleFR FROM [{databaseName}].dbo.[{table}] WHERE ID = @id;";
                command.Parameters.AddWithValue("@id", id);
                object? stored = command.ExecuteScalar();
                Assert(stored != null && (stored == DBNull.Value ? null : (string)stored) == expected, "Persistence must preserve the raw translation independently of fallback reads.");
            }
        }

        /// <summary>Ελέγχει fallback σε SQL projections και φορτωμένα related entities.</summary>
        /// <param name="database">Η ενεργή προσωρινή βάση.</param>
        /// <exception cref="Exception">Αποτυγχάνει ο έλεγχος σχέσης ή η επικοινωνία με τη βάση.</exception>
        private void CheckRelatedReads(BlueprintDatabase database)
        {
            database.RegisterEntity<FallbackReference>();
            SqlFallbackRecord product = SqlFallbackRecord.Where(item => item.Code == 0).Single();
            FallbackReference reference = FallbackReference.Create();
            reference.ProductID = product.ID;
            FallbackReference.Insert(reference);
            Assert(FallbackReference.Where(item => item.Product.Title == "Zulu", SELECTED_LANGUAGE_ID).Select(item => item.Product.Title).Single() == "Zulu", "Related SQL predicates and projections must resolve fallback.");
            FallbackReference loaded = FallbackReference.SingleOrDefault(item => item.ID == reference.ID, SELECTED_LANGUAGE_ID)!;
            Assert(loaded.Product.Title == "Zulu" && loaded.Product.CurrentLanguage == SELECTED_LANGUAGE_ID, "Related materialized entities must use the query language and fallback.");
        }
    }

    /// <summary>Ορίζει κοινό API πεδίων για τους ίδιους ελέγχους με και χωρίς cache.</summary>
    public interface FallbackFields
    {
        int Code { get; set; }

        int Marker { get; set; }

        [Multilingual, FallbackToDefaultLanguage]
        string? Title { get; set; }

        [Multilingual]
        string? PlainTitle { get; set; }
    }

    /// <summary>Διαβάζει τα πεδία απευθείας από SQL.</summary>
    [Entity("FallbackSqlRecords")]
    public abstract class SqlFallbackRecord : BlueprintEntityDB, FallbackFields
    {
        public abstract int Code { get; set; }

        public abstract int Marker { get; set; }

        [Multilingual, FallbackToDefaultLanguage]
        public abstract string? Title { get; set; }

        [Multilingual]
        public abstract string? PlainTitle { get; set; }
    }

    /// <summary>Διαβάζει τα ίδια πεδία από RAM cache.</summary>
    [Entity("FallbackCachedRecords")]
    public abstract class CachedFallbackRecord : BlueprintEntityRAM, FallbackFields
    {
        public abstract int Code { get; set; }

        public abstract int Marker { get; set; }

        [Multilingual, FallbackToDefaultLanguage]
        public abstract string? Title { get; set; }

        [Multilingual]
        public abstract string? PlainTitle { get; set; }
    }

    /// <summary>Ορίζει σχέση για έλεγχο μεταφράσεων κατά το SQL materialization.</summary>
    [Entity("FallbackReferences")]
    public abstract class FallbackReference : BlueprintEntityDB
    {
        public abstract long ProductID { get; set; }

        [ForeignKey(nameof(ProductID)), AutoInclude]
        public abstract SqlFallbackRecord Product { get; set; }
    }

    /// <summary>Δοκιμάζει την απόρριψη fallback χωρίς Multilingual.</summary>
    [Entity("UntranslatedFallback")]
    public abstract class UntranslatedFallback : BlueprintEntityDB
    {
        [FallbackToDefaultLanguage]
        public abstract string? Title { get; set; }
    }

    /// <summary>Δοκιμάζει την απόρριψη fallback σε μη κειμενικό πεδίο.</summary>
    [Entity("NonStringFallback")]
    public abstract class NonStringFallback : BlueprintEntityDB
    {
        [Multilingual, FallbackToDefaultLanguage]
        public abstract int Title { get; set; }
    }

    /// <summary>Δοκιμάζει την απόρριψη fallback σε μη αποθηκευμένο πεδίο.</summary>
    [Entity("UnmappedFallback")]
    public abstract class UnmappedFallback : BlueprintEntityDB
    {
        [NotMapped, Multilingual, FallbackToDefaultLanguage]
        public abstract string? Title { get; set; }
    }
}
