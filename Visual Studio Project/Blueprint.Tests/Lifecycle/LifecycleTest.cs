using Blueprint.BackgroundTasks;
using Blueprint.BackgroundTasks.Schedule;
using Blueprint.Database.Entities.Annotations;
using Blueprint.Database.Entities;
using Blueprint.Database;
using Blueprint.Database.Sql;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace Blueprint.Tests.Lifecycle
{
    /// <summary>Ελέγχει μέσω των δημόσιων APIs την αυτόματη ενεργοποίηση της βάσης και τον ανεξάρτητο κύκλο ζωής των managers.</summary>
    internal sealed class LifecycleTest : BlueprintTest
    {
        private readonly bool fUseSql;

        /// <summary>Επιλέγει αν θα εκτελεστούν έλεγχοι CRUD σε προσωρινή βάση SQL Server.</summary>
        /// <param name="useSql">Αν θα ακολουθήσουν έλεγχοι SQL μετά τους ανεξάρτητους ελέγχους.</param>
        public LifecycleTest(bool useSql)
        {
            fUseSql = useSql;
        }

        /// <summary>Ελέγχει την ενεργοποίηση, την ανάκαμψη από αποτυχία, τις γλώσσες και τον τερματισμό του manager.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος κύκλου ζωής ή λειτουργία SQL.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            CheckDatabaseLifecycle();
            CheckConcurrentActivation();
            CheckBackgroundManager();
            if (fUseSql)
            {
                CheckSqlEntityCalls();
            }
            Console.WriteLine($"Lifecycle checks passed: {getAssertionCount()} assertions.");
        }

        /// <summary>Ελέγχει την ενεργοποίηση μετά την αρχικοποίηση, τις ανεξάρτητες γλώσσες και την αποδέσμευση χωρίς σύνδεση SQL.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος κύκλου ζωής.</exception>
        private void CheckDatabaseLifecycle()
        {
            AssertThrows<InvalidOperationException>(() => LifecycleRecord.Create(), "Entity calls must reject a missing active database.");
            AssertThrows<InvalidOperationException>(() => new ModelDatabase(CreateLanguages(), true), "Failed initialization must propagate its error.");
            AssertThrows<InvalidOperationException>(() => LifecycleRecord.Create(), "Failed initialization must not publish a database.");
            List<BlueprintLanguage> languages = CreateLanguages();
            ModelDatabase previousDatabase;
            IQueryable<LifecycleRecord> previousQuery;
            using (ModelDatabase database = new ModelDatabase(languages))
            {
                previousDatabase = database;
                languages.Clear();
                Assert(database.Languages.Count == 2, "The database must own its language snapshot.");
                AssertThrows<NotSupportedException>(() => ((IList<BlueprintLanguage>)database.Languages).Clear(), "Languages must be read-only.");
                AssertThrows<InvalidOperationException>(() => LifecycleRecord.Create(), "Entities must be registered before use.");
                database.RegisterEntity<LifecycleRecord>();
                LifecycleRecord record = LifecycleRecord.Create();
                Assert(record.CurrentLanguage == 7, "Entity creation must use the database's default language.");
                record.Title = "Default";
                record.CurrentLanguage = 9;
                record.Title = "Translated";
                record.CurrentLanguage = 7;
                Assert(record.Title == "Default", "Language changes must preserve each translation.");
                previousQuery = LifecycleRecord.Where(item => item.ID > 0);
                Assert(previousQuery.ToQueryString().Contains("LifecycleRecords"), "Entity queries must use the active database model.");
                AssertThrows<InvalidOperationException>(() => new ModelDatabase(CreateLanguages()), "A second active database must be rejected.");
                Assert(LifecycleRecord.Create().CurrentLanguage == 7, "Rejected activation must preserve the original database.");
                database.RegisterEntity<LifecycleRecord>();
                Assert(LifecycleRecord.Create().CurrentLanguage == 7, "Repeated registration must preserve activation.");
                using (BackgroundTasksManager manager = new BackgroundTasksManager())
                {
                    manager.Start();
                    manager.Stop();
                    Assert(LifecycleRecord.Create().CurrentLanguage == 7, "Stopping an independent manager must not dispose the database.");
                }
            }
            AssertThrows<InvalidOperationException>(() => LifecycleRecord.Create(), "Disposal must clear the active database.");
            AssertThrows<ObjectDisposedException>(() => previousQuery.ToList(), "Deferred queries must retain their disposed original database.");
            using (ModelDatabase database = new ModelDatabase(CreateLanguages()))
            {
                database.RegisterEntity<LifecycleRecord>();
                previousDatabase.Dispose();
                Assert(LifecycleRecord.Create().CurrentLanguage == 7, "Repeated disposal must not clear a replacement database.");
            }
        }

        /// <summary>Επαληθεύει ότι η ταυτόχρονη αρχικοποίηση ενεργοποιεί ακριβώς μία βάση.</summary>
        /// <exception cref="Exception">Αποτυγχάνει ο συγχρονισμός των constructors ή η επιλογή της ενεργής βάσης.</exception>
        private void CheckConcurrentActivation()
        {
            using (Barrier barrier = new Barrier(2))
            {
                Task<ModelDatabase?> first = Task.Run(() => TryCreateDatabase(barrier));
                Task<ModelDatabase?> second = Task.Run(() => TryCreateDatabase(barrier));
                ModelDatabase?[] databases = Task.WhenAll(first, second).GetAwaiter().GetResult();
                try
                {
                    Assert(databases.Count(database => database != null) == 1, "Exactly one concurrent constructor must activate its database.");
                    ModelDatabase database = databases.Single(database => database != null)!;
                    database.RegisterEntity<LifecycleRecord>();
                    Assert(LifecycleRecord.Create().CurrentLanguage == 7, "The losing constructor must not clear the winner's activation.");
                }
                finally
                {
                    foreach (ModelDatabase? database in databases)
                    {
                        database?.Dispose();
                    }
                }
            }
        }

        /// <summary>Δοκιμάζει συγχρονισμένη αρχικοποίηση, επιτρέποντας την απόρριψη δεύτερης ενεργής βάσης.</summary>
        /// <param name="barrier">Το σημείο συγχρονισμού πριν ενεργοποιηθεί οποιαδήποτε βάση.</param>
        /// <returns>Η βάση που ενεργοποιήθηκε ή null αν προηγήθηκε άλλος constructor.</returns>
        private static ModelDatabase? TryCreateDatabase(Barrier barrier)
        {
            try
            {
                return new ModelDatabase(CreateLanguages(), barrier: barrier);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>Ελέγχει εκκίνηση, παύση, επανεκκίνηση, προστασία των callbacks και αναμονή κατά την αποδέσμευση χωρίς ενεργή βάση.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος ή η εργασία δεν ολοκληρώνεται εντός του χρονικού ορίου.</exception>
        private void CheckBackgroundManager()
        {
            int executions = 0;
            using (BackgroundTasksManager manager = new BackgroundTasksManager())
            {
                CallbackTask task = new CallbackTask(() => Interlocked.Increment(ref executions));
                AssertThrows<InvalidOperationException>(() => manager.RegisterTask(task), "A manager must start before accepting tasks.");
                manager.Start();
                manager.Start();
                manager.RegisterTask(task);
                Assert(manager.RunTaskNow(task.Name), "Standalone task execution must work without a database.");
                manager.Pause();
                Assert(executions == 1 && manager.Tasks.Count == 1, "Pause must wait for work and preserve registrations.");
                AssertThrows<InvalidOperationException>(() => manager.RunTaskNow(task.Name), "A paused manager must reject new work.");
                manager.Start();
                Assert(manager.RunTaskNow(task.Name), "A paused manager must resume.");
                manager.Stop();
                Assert(executions == 2 && manager.Tasks.Count == 0, "Stop must wait for work and release registrations.");
                manager.Stop();
                AssertThrows<ObjectDisposedException>(() => manager.Start(), "A stopped manager must not restart.");
            }

            using (BackgroundTasksManager manager = new BackgroundTasksManager())
            {
                Exception? failure = null;
                manager.TaskFailed += (task, exception) => failure = exception;
                manager.Start();
                manager.RegisterTask(new CallbackTask(() => manager.Stop()));
                manager.RunTaskNow("Lifecycle task");
                manager.Pause();
                Assert(failure is InvalidOperationException, "Stopping a manager from its task must fail without deadlocking.");
            }

            using (ManualResetEventSlim entered = new ManualResetEventSlim())
            using (ManualResetEventSlim release = new ManualResetEventSlim())
            using (ManualResetEventSlim disposalStarted = new ManualResetEventSlim())
            using (BackgroundTasksManager manager = new BackgroundTasksManager())
            {
                manager.Start();
                manager.RegisterTask(new CallbackTask(() =>
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("The test did not release the background task.");
                    }
                }));
                manager.RunTaskNow("Lifecycle task");
                Task? disposal = null;
                try
                {
                    Assert(entered.Wait(TimeSpan.FromSeconds(5)), "The background task must start.");
                    disposal = Task.Run(() =>
                    {
                        disposalStarted.Set();
                        manager.Dispose();
                    });
                    Assert(disposalStarted.Wait(TimeSpan.FromSeconds(5)), "Disposal must start.");
                    Assert(!disposal.Wait(TimeSpan.FromMilliseconds(100)), "Disposal must wait for the running task.");
                }
                finally
                {
                    release.Set();
                    Assert(disposal == null || disposal.Wait(TimeSpan.FromSeconds(5)), "Disposal must finish after its task completes.");
                }
            }
        }

        /// <summary>Ελέγχει τις υπάρχουσες στατικές κλήσεις CRUD σε προσωρινή βάση SQL με μοναδικό όνομα.</summary>
        /// <exception cref="Exception">Αποτυγχάνει η προετοιμασία SQL, έλεγχος CRUD ή η διαγραφή της προσωρινής βάσης.</exception>
        private void CheckSqlEntityCalls()
        {
            string databaseName = "BlueprintLifecycle_" + Guid.NewGuid().ToString("N");
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
                    using (BlueprintDatabase database = new SqlDatabase(databaseName, settings.ConnectionString, CreateLanguages()))
                    {
                        database.RegisterEntity<LifecycleRecord>();
                        LifecycleRecord first = LifecycleRecord.Create();
                        first.Title = "First";
                        first.CurrentLanguage = 9;
                        first.Title = "Translated";
                        LifecycleRecord.Insert(first);
                        Assert(first.ID > 0 && LifecycleRecord.Any(), "Static Insert and Any must use the active database.");
                        Assert(LifecycleRecord.SingleOrDefault(item => item.ID == first.ID, 9)!.Title == "Translated", "Language-specific reads must use the active database.");
                        LifecycleRecord second = LifecycleRecord.Create();
                        second.Title = "Second";
                        LifecycleRecord.Insert(new[] { second });
                        Assert(LifecycleRecord.Where(item => item.ID > 0).Count() == 2, "Batch insertion and Where must remain available.");
                        second.Title = "Updated";
                        LifecycleRecord.Update(second);
                        Assert(LifecycleRecord.FirstOrDefault(item => item.ID == second.ID)!.Title == "Updated", "Static Update and FirstOrDefault must remain available.");
                        first.CurrentLanguage = 7;
                        first.Title = "Batch updated";
                        LifecycleRecord.Update(new[] { first });
                        Assert(LifecycleRecord.Any(item => item.Title == "Batch updated"), "Batch update and predicate Any must remain available.");
                        LifecycleRecord.Delete(item => item.ID == second.ID);
                        Assert(LifecycleRecord.SingleOrDefault()!.ID == first.ID, "Static Delete and SingleOrDefault must remain available.");
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
        }

        /// <summary>Δημιουργεί ρύθμιση γλωσσών με προεπιλεγμένο ID διαφορετικό από το ένα.</summary>
        /// <returns>Οι γλώσσες που χρησιμοποιούνται στους ελέγχους κύκλου ζωής.</returns>
        private static List<BlueprintLanguage> CreateLanguages()
        {
            return new List<BlueprintLanguage> { new BlueprintLanguage(7, string.Empty), new BlueprintLanguage(9, "GR") };
        }

        /// <summary>Αρχικοποιεί μοντέλα EF χωρίς σύνδεση, απομονώνοντας τη συμπεριφορά του κύκλου ζωής της βάσης.</summary>
        private sealed class ModelDatabase : BlueprintDatabase
        {
            private readonly bool fFailInitialization;
            private readonly Barrier? fBarrier;

            /// <summary>Αρχικοποιεί τον provider, με προαιρετική αποτυχία ή συγχρονισμό ταυτόχρονης κατασκευής.</summary>
            /// <param name="languages">Οι γλώσσες που αντιγράφονται στη βάση.</param>
            /// <param name="failInitialization">Αν πρέπει να αποτύχει η αρχικοποίηση του schema.</param>
            /// <param name="barrier">Προαιρετικό σημείο συγχρονισμού πριν την ενεργοποίηση.</param>
            /// <exception cref="Exception">Αποτυγχάνει η αρχικοποίηση του provider ή η ταυτόχρονη ενεργοποίηση.</exception>
            public ModelDatabase(List<BlueprintLanguage> languages, bool failInitialization = false, Barrier? barrier = null) : base("LifecycleModel", "Server=unused;Database=LifecycleModel", languages)
            {
                fFailInitialization = failInitialization;
                fBarrier = barrier;
                try
                {
                    SynchronizeEntities();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            /// <summary>Απορρίπτει την εκτέλεση εντολών επειδή ο test provider λειτουργεί χωρίς σύνδεση βάσης.</summary>
            /// <param name="commandText">Η εντολή που δεν θα εκτελεστεί.</param>
            /// <param name="parameters">Οι παράμετροι της εντολής.</param>
            /// <param name="commandTimeoutSeconds">Το προαιρετικό χρονικό όριο εκτέλεσης.</param>
            /// <returns>Δεν επιστρέφεται τιμή, επειδή η λειτουργία δεν υποστηρίζεται.</returns>
            /// <exception cref="NotSupportedException">Ο provider χρησιμοποιείται μόνο για κατασκευή μοντέλων.</exception>
            public override int ExecuteNonQuery(string commandText, IReadOnlyList<DbParameter>? parameters = null, int? commandTimeoutSeconds = null)
            {
                throw new NotSupportedException("The model-only test provider does not execute database commands.");
            }

            /// <summary>Κατασκευάζει το μοντέλο entities και προσομοιώνει επιτυχία ή αποτυχία χωρίς πρόσβαση σε SQL Server.</summary>
            /// <exception cref="InvalidOperationException">Αποτυγχάνει η προσομοιωμένη αρχικοποίηση.</exception>
            /// <exception cref="TimeoutException">Οι ταυτόχρονοι constructors δεν φτάνουν στο σημείο συγχρονισμού.</exception>
            protected override void SynchronizeEntities()
            {
                InitializeContext(new DbContextOptionsBuilder().UseSqlServer(ConnectionString), context =>
                {
                    if (fFailInitialization)
                    {
                        throw new InvalidOperationException("Simulated provider failure.");
                    }
                    if (fBarrier != null && fBarrier.CurrentPhaseNumber == 0 && !fBarrier.SignalAndWait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("Concurrent database construction did not reach the rendezvous.");
                    }
                });
            }
        }

        /// <summary>Εκτελεί δοσμένη ενέργεια μέσω του δημόσιου API των background tasks.</summary>
        private sealed class CallbackTask : BackgroundTask
        {
            private readonly Action fAction;

            /// <summary>Αποθηκεύει εργασία για χειροκίνητη εκτέλεση με χρονοδιάγραμμα εκτός της διάρκειας της δοκιμής.</summary>
            /// <param name="action">Η ενέργεια που εκτελεί η εργασία.</param>
            public CallbackTask(Action action) : base("Lifecycle task", "Checks independent manager lifetime.", new IntervalSchedule(TimeSpan.FromDays(1)))
            {
                fAction = action;
            }

            /// <summary>Εκτελεί τον δοσμένο έλεγχο κύκλου ζωής.</summary>
            /// <exception cref="Exception">Αποτυγχάνει η δοσμένη ενέργεια.</exception>
            public override void Run()
            {
                fAction();
            }
        }
    }

    /// <summary>Ορίζει ελάχιστο πολύγλωσσο entity για ελέγχους κύκλου ζωής και CRUD.</summary>
    [Entity("LifecycleRecords")]
    public abstract class LifecycleRecord : BlueprintEntityDB
    {
        [Multilingual]
        public abstract string Title { get; set; }
    }
}
