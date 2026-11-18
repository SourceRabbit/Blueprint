using Blueprint.Database;
using Blueprint.Entities;
using Blueprint.Database.Sql;
using Blueprint.Database.TransactionFactory;
using Blueprint.Fundamentals;
using Blueprint.Tests.RAMCache;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Tests.Sql
{
    /// <summary>Ελέγχει ανεξάρτητες SQL εντολές και τη συνεργασία τους με παραμέτρους, transactions και RAM cache.</summary>
    internal sealed class ExecuteNonQueryTest : BlueprintTest
    {
        private const string WAIT_COMMAND = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'ExecuteNonQueryGate', @LockMode=N'Shared', @LockOwner=N'Session', @LockTimeout=15000; IF @result < 0 THROW 51000, N'Test gate timed out.', 1; SET @session = @@SPID; EXEC sys.sp_releaseapplock @Resource=N'ExecuteNonQueryGate', @LockOwner=N'Session';";

        /// <summary>Εκτελεί τους ελέγχους σε προσωρινή βάση και διαγράφει μόνο τα δεδομένα αυτής της δοκιμής.</summary>
        /// <exception cref="Exception">Αποτυγχάνει κάποιος έλεγχος, η επικοινωνία με SQL ή ο καθαρισμός της βάσης.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            string databaseName = "BlueprintExecuteNonQuery_" + Guid.NewGuid().ToString("N");
            string connectionString = Environment.GetEnvironmentVariable("BLUEPRINT_TEST_SQL_CONNECTION_STRING") ?? fDatabaseConnectionString;
            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", Pooling = false, ConnectTimeout = 5, Enlist = false };
            using (SqlConnection master = new SqlConnection(settings.ConnectionString))
            {
                master.Open();
                try
                {
                    List<BlueprintLanguage> languages = new List<BlueprintLanguage> { new BlueprintLanguage(1, string.Empty), new BlueprintLanguage(2, "EN") };
                    using (BlueprintDatabase database = new SqlDatabase(databaseName, settings.ConnectionString, languages))
                    {
                        database.RAMCacheRefreshInterval = TimeSpan.FromHours(1);
                        database.RegisterEntity<CachedRecord>();
                        CheckCommands(database);
                        CheckValidation(database);
                        CheckTransactions(database);
                        settings.InitialCatalog = databaseName;
                        using (SqlConnection observer = new SqlConnection(settings.ConnectionString))
                        {
                            observer.Open();
                            CheckConcurrency(database, observer);
                            CheckDisposal(database, observer);
                        }
                    }
                }
                finally
                {
                    using (SqlCommand command = master.CreateCommand())
                    {
                        command.CommandText = $"IF DB_ID(@name) IS NOT NULL DROP DATABASE [{databaseName}];";
                        command.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = databaseName;
                        command.ExecuteNonQuery();
                    }
                }
            }
            Console.WriteLine($"ExecuteNonQuery checks passed: {getAssertionCount()} assertions.");
        }

        /// <summary>Ελέγχει πραγματικές εγγραφές, παραμέτρους, SQL σφάλματα και άμεση ανανέωση της cache.</summary>
        /// <param name="database">Η προσωρινή ενεργή βάση.</param>
        /// <exception cref="Exception">Αποτυγχάνει SQL εντολή ή έλεγχος αποτελέσματος.</exception>
        private void CheckCommands(BlueprintDatabase database)
        {
            Assert(CachedRecord.Where(item => true).Count() == 0, "The initial RAM snapshot must be empty.");
            string title = "Προϊόν '320'; DROP TABLE dbo.RAMCachedRecords; --";
            SqlParameter price = new SqlParameter("@price", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = 10.25m };
            SqlParameter text = new SqlParameter("@title", SqlDbType.NVarChar, -1) { Value = title };
            SqlParameter translation = new SqlParameter("@translation", SqlDbType.NVarChar, -1) { Value = null };
            DbParameter[] parameters = new DbParameter[] { price, text, translation };
            const string INSERT = "INSERT INTO dbo.RAMCachedRecords (Price, Title, TitleEN) VALUES (@price, @title, @translation);";
            Assert(database.ExecuteNonQuery(INSERT, parameters) == 1, "Insert must return the affected-row count.");
            CachedRecord first = CachedRecord.Where(item => true).Single();
            Assert(first.Price == 10.25m && first.Title == title, "Typed parameters must preserve decimal and literal Unicode values.");
            Assert(CachedRecord.Where(item => item.ID == first.ID, 2).Single().Title == null, "A null parameter value must be stored as SQL NULL.");
            price.Value = 20m;
            Assert(database.ExecuteNonQuery(INSERT, parameters) == 1, "The same parameter instances must be reusable after a call.");
            Assert(CachedRecord.Where(item => true).Count() == 2, "The next read must synchronize cache without waiting one hour.");

            SqlParameter id = new SqlParameter("@id", SqlDbType.BigInt) { Value = first.ID };
            Assert(database.ExecuteNonQuery("UPDATE dbo.RAMCachedRecords SET Price = 15 WHERE ID = @id;", new DbParameter[] { id }) == 1, "Update must return one affected row.");
            Assert(CachedRecord.Where(item => item.ID == first.ID).Single().Price == 15m, "Update must invalidate the existing RAM snapshot.");
            Assert(first.Price == 10.25m, "Previously returned entity copies must remain independent.");
            Assert(database.ExecuteNonQuery("DELETE FROM dbo.RAMCachedRecords WHERE ID = -1;") == 0, "Zero affected rows must be a successful result.");

            AssertThrows<SqlException>(() => database.ExecuteNonQuery("UPDATE dbo.RAMCachedRecords SET Price = 30 WHERE ID = @id; THROW 51000, N'Expected failure after commit.', 1;", new DbParameter[] { id }), "SQL errors must propagate after a partially committed batch.");
            Assert(CachedRecord.Where(item => item.ID == first.ID).Single().Price == 30m, "A failed batch must invalidate cache for writes already committed.");
            AssertThrows<SqlException>(() => database.ExecuteNonQuery("BEGIN TRANSACTION; UPDATE dbo.RAMCachedRecords SET Price = 999 WHERE ID = @id; THROW 51000, N'Expected rollback on disconnect.', 1;", new DbParameter[] { id }), "A failed command must release its independent connection.");
            Assert(CachedRecord.Where(item => item.ID == first.ID).Single().Price == 30m, "Closing a failed connection must not publish its uncommitted changes.");

            Assert(database.ExecuteNonQuery("SET NOCOUNT ON; UPDATE dbo.RAMCachedRecords SET Price = 35 WHERE ID = @id;", new DbParameter[] { id }) == -1, "The provider's unknown row count must remain -1.");
            Assert(CachedRecord.Where(item => item.ID == first.ID).Single().Price == 35m, "Unknown affected-row counts must still invalidate cache.");
            SqlParameter output = new SqlParameter("@value", SqlDbType.Int) { Direction = ParameterDirection.Output };
            Assert(database.ExecuteNonQuery("SET @value = 42;", new DbParameter[] { output }, 0) == -1 && (int)output.Value == 42, "Output parameters and an unlimited timeout must be supported.");
            Assert(database.ExecuteNonQuery("DELETE FROM dbo.RAMCachedRecords WHERE ID = @id;", new DbParameter[] { id }) == 1, "Parameters must remain reusable after SQL errors.");
            Assert(!CachedRecord.Where(item => item.ID == first.ID).Any(), "Delete must remove the row from the next cached result.");
        }

        /// <summary>Ελέγχει άκυρα ορίσματα, αποδέσμευση παραμέτρων και εφαρμογή του command timeout.</summary>
        /// <param name="database">Η προσωρινή ενεργή βάση.</param>
        /// <exception cref="Exception">Αποτυγχάνει η αναμενόμενη απόρριψη ή το timeout.</exception>
        private void CheckValidation(BlueprintDatabase database)
        {
            AssertThrows<ArgumentNullException>(() => database.ExecuteNonQuery(null!), "Null command text must be rejected.");
            AssertThrows<ArgumentException>(() => database.ExecuteNonQuery("  "), "Whitespace command text must be rejected.");
            AssertThrows<ArgumentOutOfRangeException>(() => database.ExecuteNonQuery("SELECT 1;", commandTimeoutSeconds: -1), "A negative timeout must be rejected.");
            SqlParameter reusable = new SqlParameter("@value", SqlDbType.Int) { Direction = ParameterDirection.Output };
            AssertThrows<ArgumentException>(() => database.ExecuteNonQuery("SET @value = 7;", new DbParameter[] { reusable, null! }), "Null parameter entries must be rejected.");
            AssertThrows<ArgumentException>(() => database.ExecuteNonQuery("SET @value = 7;", new DbParameter[] { reusable, new UnsupportedParameter() }), "Parameters from incompatible providers must be rejected.");
            using (SqlCommand owner = new SqlCommand())
            {
                SqlParameter owned = owner.Parameters.Add("@owned", SqlDbType.Int);
                owned.Value = 1;
                AssertThrows<ArgumentException>(() => database.ExecuteNonQuery("SET @value = 7;", new DbParameter[] { reusable, owned }), "Parameters already attached to another command must be rejected.");
                Assert(owner.Parameters.Contains(owned), "Rejecting an owned parameter must preserve its original command.");
            }
            database.ExecuteNonQuery("SET @value = 7;", new DbParameter[] { reusable });
            Assert((int)reusable.Value == 7, "Earlier parameters must be detached when a later parameter fails validation.");
            try
            {
                database.ExecuteNonQuery("WAITFOR DELAY '00:00:05';", commandTimeoutSeconds: 1);
                throw new InvalidOperationException("The command timeout was not applied.");
            }
            catch (SqlException exception)
            {
                Assert(exception.Number == -2, "The SQL failure must be a command timeout.");
            }
            Assert(database.ExecuteNonQuery("DELETE FROM dbo.RAMCachedRecords WHERE ID = -1;") == 0, "A timeout must not prevent later standalone commands.");
        }

        /// <summary>Ελέγχει ότι οι ανεξάρτητες εντολές δεν παρακάμπτουν τα όρια των application transactions.</summary>
        /// <param name="database">Η προσωρινή ενεργή βάση.</param>
        /// <exception cref="Exception">Αποτυγχάνει η απόρριψη εντολής ή η επαναφορά του transaction.</exception>
        private void CheckTransactions(BlueprintDatabase database)
        {
            using (System.Transactions.TransactionScope scope = new System.Transactions.TransactionScope())
            {
                AssertThrows<InvalidOperationException>(() => database.ExecuteNonQuery("DELETE FROM dbo.RAMCachedRecords;"), "Ambient transactions must be rejected before opening a connection.");
            }
            DatabaseTransaction preparation = database.TransactionFactory.CreateNewTransaction(pending =>
            {
                database.ExecuteNonQuery("DELETE FROM dbo.RAMCachedRecords;");
            });
            AssertThrows<InvalidOperationException>(() => database.TransactionFactory.ExecuteTransactionAsync(preparation).GetAwaiter().GetResult(), "Preparation callbacks must reject standalone commands.");
            CachedRecord record = CachedRecord.Where(item => true).Single();
            decimal originalPrice = record.Price;
            record.Price = 99m;
            DatabaseTransaction validation = database.TransactionFactory.CreateNewTransaction();
            validation.AddUpdate(record);
            validation.AddValidation(() => database.ExecuteNonQuery("DELETE FROM dbo.RAMCachedRecords;"));
            AssertThrows<InvalidOperationException>(() => database.TransactionFactory.ExecuteTransactionAsync(validation).GetAwaiter().GetResult(), "Validation callbacks must reject standalone commands.");
            Assert(CachedRecord.Where(item => true).Single().Price == originalPrice, "The rejected validation must roll back its queued update.");
        }

        /// <summary>Κρατά δύο εντολές ενεργές στον SQL Server και ελέγχει ότι δεν δεσμεύουν το κοινό context.</summary>
        /// <param name="database">Η προσωρινή ενεργή βάση.</param>
        /// <param name="observer">Η σύνδεση που ελέγχει το SQL σημείο αναμονής.</param>
        /// <exception cref="Exception">Δεν εκτελούνται παράλληλα οι εντολές ή εμποδίζεται η πρόσβαση στο cache.</exception>
        private void CheckConcurrency(BlueprintDatabase database, SqlConnection observer)
        {
            SetGate(observer, true);
            SqlParameter firstSession = new SqlParameter("@session", SqlDbType.Int) { Direction = ParameterDirection.Output };
            SqlParameter secondSession = new SqlParameter("@session", SqlDbType.Int) { Direction = ParameterDirection.Output };
            Task<int> first = Task.Run(() => database.ExecuteNonQuery(WAIT_COMMAND, new DbParameter[] { firstSession }, 20));
            Task<int> second = Task.Run(() => database.ExecuteNonQuery(WAIT_COMMAND, new DbParameter[] { secondSession }, 20));
            Task<int>? read = null;
            try
            {
                Assert(WaitForCommands(observer, 2), "Two independent SQL commands must reach the server concurrently.");
                read = Task.Run(() => CachedRecord.Where(item => true).Count());
                Assert(read.Wait(TimeSpan.FromSeconds(3)) && read.Result == 1, "RAM queries must remain available while standalone SQL commands are waiting.");
            }
            finally
            {
                SetGate(observer, false);
                Task.WhenAll(first, second).GetAwaiter().GetResult();
                read?.GetAwaiter().GetResult();
            }
            Assert((int)firstSession.Value != (int)secondSession.Value, "Concurrent commands must use different SQL sessions.");
        }

        /// <summary>Ελέγχει ότι το Dispose απορρίπτει νέες κλήσεις και επιτρέπει να ολοκληρωθεί ήδη αποδεκτή εντολή.</summary>
        /// <param name="database">Η προσωρινή βάση που θα αποδεσμευτεί.</param>
        /// <param name="observer">Η ανεξάρτητη σύνδεση ελέγχου.</param>
        /// <exception cref="Exception">Αποτυγχάνει η αποδέσμευση ή η εντολή που ήταν ήδη ενεργή.</exception>
        private void CheckDisposal(BlueprintDatabase database, SqlConnection observer)
        {
            SetGate(observer, true);
            SqlParameter session = new SqlParameter("@session", SqlDbType.Int) { Direction = ParameterDirection.Output };
            Task<int> pending = Task.Run(() => database.ExecuteNonQuery(WAIT_COMMAND, new DbParameter[] { session }, 20));
            Task? disposal = null;
            try
            {
                Assert(WaitForCommands(observer, 1), "The independent command must start before disposal.");
                disposal = Task.Run(() => database.Dispose());
                Assert(disposal.Wait(TimeSpan.FromSeconds(3)), "Disposal must not wait for the independent SQL command.");
                AssertThrows<ObjectDisposedException>(() => database.ExecuteNonQuery("SELECT 1;"), "A disposed database must reject new standalone calls.");
            }
            finally
            {
                SetGate(observer, false);
                pending.GetAwaiter().GetResult();
                disposal?.GetAwaiter().GetResult();
            }
            Assert(session.Value is int, "The already accepted command must finish after database disposal.");
        }

        /// <summary>Δεσμεύει ή απελευθερώνει ένα SQL application lock μόνο μέσα στην προσωρινή βάση.</summary>
        /// <param name="observer">Η σύνδεση που διατηρεί το lock.</param>
        /// <param name="acquire">Αν πρέπει να δεσμευτεί ή να απελευθερωθεί το lock.</param>
        /// <exception cref="Exception">Αποτυγχάνει η διαχείριση του lock.</exception>
        private void SetGate(SqlConnection observer, bool acquire)
        {
            using (SqlCommand command = observer.CreateCommand())
            {
                command.CommandText = acquire ? "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'ExecuteNonQueryGate', @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=5000; SELECT @result;" : "DECLARE @result int; EXEC @result = sys.sp_releaseapplock @Resource=N'ExecuteNonQueryGate', @LockOwner=N'Session'; SELECT @result;";
                Assert((int)command.ExecuteScalar()! >= 0, "The SQL test gate must be acquired or released successfully.");
            }
        }

        /// <summary>Περιμένει μέχρι να φτάσουν οι εντολές στο SQL σημείο αναμονής χωρίς χρονικές υποθέσεις εκκίνησης.</summary>
        /// <param name="observer">Η σύνδεση που παρατηρεί τα application locks της προσωρινής βάσης.</param>
        /// <param name="expectedCount">Το αναμενόμενο πλήθος εντολών σε αναμονή.</param>
        /// <returns>True αν οι εντολές φτάσουν στο σημείο αναμονής εντός πέντε δευτερολέπτων.</returns>
        /// <exception cref="SqlException">Αποτυγχάνει η ανάγνωση των SQL locks.</exception>
        private static bool WaitForCommands(SqlConnection observer, int expectedCount)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            using (SqlCommand command = observer.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(DISTINCT request_session_id) FROM sys.dm_tran_locks WHERE resource_database_id = DB_ID() AND resource_type = 'APPLICATION' AND request_status = 'WAIT';";
                while (timeout.Elapsed < TimeSpan.FromSeconds(5))
                {
                    if ((int)command.ExecuteScalar()! >= expectedCount)
                    {
                        return true;
                    }
                    Thread.Sleep(20);
                }
            }
            return false;
        }

        /// <summary>Παριστάνει παράμετρο άλλου provider χωρίς εξάρτηση από δεύτερο database driver.</summary>
        private sealed class UnsupportedParameter : DbParameter
        {
            /// <summary>Απορρίπτει χρήση της παραμέτρου πέρα από τον έλεγχο συμβατότητας.</summary>
            /// <exception cref="NotSupportedException">Η παράμετρος χρησιμοποιείται μόνο για έλεγχο απόρριψης.</exception>
            public override void ResetDbType()
            {
                throw new NotSupportedException();
            }

            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; }
            public override bool IsNullable { get; set; }
            [AllowNull]
            public override string ParameterName { get; set; } = "@unsupported";
            [AllowNull]
            public override string SourceColumn { get; set; } = string.Empty;
            public override object? Value { get; set; }
            public override bool SourceColumnNullMapping { get; set; }
            public override int Size { get; set; }
        }
    }
}
