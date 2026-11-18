using Blueprint.BackgroundTasks;
using Blueprint.BackgroundTasks.Schedule;
using Blueprint.Entities.Annotations;
using Blueprint.Entities;
using Blueprint.Database;
using Blueprint.Database.Sql;
using Blueprint.Database.TransactionFactory;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Reflection;

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
            CheckContextPool();
            CheckConcurrentActivation();
            CheckTransactionLifecycle();
            CheckDatabaseDisposal();
            CheckCacheCallbacks();
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
            Assert(BlueprintDatabase.ACTIVE_INSTANCE == null, "The active instance must be null before construction.");
            AssertThrows<InvalidOperationException>(() => new ModelDatabase(CreateLanguages(), true), "Failed initialization must propagate its error.");
            Assert(BlueprintDatabase.ACTIVE_INSTANCE == null, "Failed initialization must clear the active instance.");
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
            Assert(BlueprintDatabase.ACTIVE_INSTANCE == null, "Disposal must clear the active instance.");
            AssertThrows<NullReferenceException>(() => previousQuery.ToList(), "Deferred queries must reject a missing active database.");
            using (ModelDatabase database = new ModelDatabase(CreateLanguages()))
            {
                database.RegisterEntity<LifecycleRecord>();
                previousDatabase.Dispose();
                Assert(LifecycleRecord.Create().CurrentLanguage == 7, "Repeated disposal must not clear a replacement database.");
            }
        }

        /// <summary>Ελέγχει τις πέντε θέσεις, την αναμονή έκτου caller και την επιστροφή μέσω Dispose και DisposeAsync.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος του pool ή ο συγχρονισμός των threads.</exception>
        private void CheckContextPool()
        {
            using (ModelDatabase database = new ModelDatabase(CreateLanguages()))
            using (ManualResetEventSlim started = new ManualResetEventSlim())
            using (ManualResetEventSlim acquired = new ManualResetEventSlim())
            using (ManualResetEventSlim finish = new ManualResetEventSlim())
            {
                database.RegisterEntity<LifecycleRecord>();
                LifecycleRecord record = LifecycleRecord.Create();
                record.ID = 42;
                BlueprintDatabaseContext[] contexts = new BlueprintDatabaseContext[5];
                Task? waiter = null;
                bool thirdReturned = false;
                bool reusedReturnedContext = false;
                bool trackingWasCleared = false;
                try
                {
                    for (int index = 0; index < contexts.Length; index++)
                    {
                        contexts[index] = database.GetContext();
                    }
                    Assert(contexts.Distinct().Count() == 5, "The pool must provide five different reserved contexts.");
                    contexts[2].Attach(record);
                    Assert(contexts[2].ChangeTracker.Entries().Any(), "The reserved context must track its own entities.");
                    waiter = Task.Run(() =>
                    {
                        started.Set();
                        using (BlueprintDatabaseContext context = database.GetContext())
                        {
                            reusedReturnedContext = ReferenceEquals(context, contexts[2]);
                            trackingWasCleared = !context.ChangeTracker.Entries().Any();
                            acquired.Set();
                            if (!finish.Wait(TimeSpan.FromSeconds(10)))
                            {
                                throw new TimeoutException("The test did not release the sixth caller.");
                            }
                        }
                    });
                    Assert(started.Wait(TimeSpan.FromSeconds(5)), "The sixth caller must start.");
                    Assert(!acquired.Wait(TimeSpan.FromMilliseconds(100)), "The sixth caller must wait while all five contexts are reserved.");
                    contexts[2].Dispose();
                    thirdReturned = true;
                    Assert(acquired.Wait(TimeSpan.FromSeconds(5)), "Returning one context must wake the waiting caller.");
                    Assert(reusedReturnedContext && trackingWasCleared, "The waiting caller must receive the returned context with empty tracking.");
                }
                finally
                {
                    finish.Set();
                    for (int index = 0; index < contexts.Length; index++)
                    {
                        if (index != 2 || !thirdReturned)
                        {
                            contexts[index]?.Dispose();
                        }
                    }
                    Assert(waiter == null || waiter.Wait(TimeSpan.FromSeconds(5)), "The waiting caller must return its context.");
                }
                BlueprintDatabaseContext asyncContext = database.GetContext();
                asyncContext.DisposeAsync().GetAwaiter().GetResult();
                using (BlueprintDatabaseContext reused = database.GetContext())
                {
                    Assert(ReferenceEquals(asyncContext, reused), "DisposeAsync must return the same reusable instance to the pool.");
                }
                for (int index = 0; index < 10; index++)
                {
                    AssertThrows<ArgumentException>(() => LifecycleRecord.Update(new LifecycleRecord[] { null! }), "Failed entity operations must not leak pool entries.");
                    Assert(LifecycleRecord.Create().CurrentLanguage == 7, "Entity operations must continue to acquire and return contexts.");
                }
                database.RegisterEntity<LifecycleRecord>();
                using (BlueprintDatabaseContext context = database.GetContext())
                {
                    Assert(context.Model.GetEntityTypes().Any(), "Schema synchronization must leave an initialized pool.");
                }
            }
        }

        /// <summary>Ελέγχει τους περιορισμούς nested υποβολής, την ακύρωση και τη μοναδική υποβολή transactions χωρίς σύνδεση SQL.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος ασφάλειας transaction.</exception>
        private void CheckTransactionLifecycle()
        {
            TransactionFactory factory;
            using (ModelDatabase database = new ModelDatabase(CreateLanguages()))
            {
                factory = database.TransactionFactory;
                DatabaseTransaction transaction = factory.CreateNewTransaction();
                DatabaseTransaction prepared = factory.CreateNewTransaction(pending => throw new InvalidOperationException("Cancelled preparation must not run."));
                database.RunUnderLock(() =>
                {
                    AssertThrows<InvalidOperationException>(() => factory.ExecuteTransactionAsync(transaction), "Submitting from inside a database operation must fail before reserving the transaction.");
                    AssertThrows<InvalidOperationException>(() => database.Dispose(), "Disposal from inside an operation must not close its context.");
                });
                using (System.Transactions.TransactionScope scope = new System.Transactions.TransactionScope())
                {
                    AssertThrows<InvalidOperationException>(() => factory.ExecuteTransactionAsync(transaction), "Submitting inside an ambient transaction must fail before reserving the transaction.");
                }
                AssertThrows<OperationCanceledException>(() => factory.ExecuteTransactionAsync(transaction, cancellationToken: new CancellationToken(true)).GetAwaiter().GetResult(), "Cancellation must run without opening SQL or executing the transaction.");
                Assert(transaction.IsCompleted && !transaction.IsCommitted, "Cancelled transactions must be completed without a commit.");
                AssertThrows<InvalidOperationException>(() => factory.ExecuteTransactionAsync(transaction), "A transaction must not be submitted twice, even after cancellation.");
                AssertThrows<InvalidOperationException>(() => transaction.AddValidation(database.Dispose), "Submitted transactions must reject further operations.");
                AssertThrows<OperationCanceledException>(() => factory.ExecuteTransactionAsync(prepared, cancellationToken: new CancellationToken(true)).GetAwaiter().GetResult(), "Cancellation must prevent the preparation callback from running.");
                Assert(prepared.IsCompleted && !prepared.IsCommitted, "Cancelled prepared transactions must publish their completion.");
                AssertThrows<ArgumentNullException>(() => factory.CreateNewTransaction(null!), "Preparation requires a non-null callback.");
                using (ManualResetEventSlim submitted = new ManualResetEventSlim())
                {
                    DatabaseTransaction queued = factory.CreateNewTransaction();
                    Task<int>? execution = null;
                    Task? submission = null;
                    database.RunUnderLock(() =>
                    {
                        submission = Task.Run(() =>
                        {
                            execution = factory.ExecuteTransactionAsync(queued, cancellationToken: new CancellationToken(true));
                            submitted.Set();
                        });
                        Assert(submitted.Wait(TimeSpan.FromSeconds(5)), "Transaction submission must reach the worker queue.");
                        Assert(SpinWait.SpinUntil(() => execution!.IsCompleted, TimeSpan.FromSeconds(5)), "A cancelled transaction must finish without waiting for the initialization lock.");
                    });
                    Assert(submission!.Wait(TimeSpan.FromSeconds(5)), "Transaction submission must finish.");
                    Assert(SpinWait.SpinUntil(() => execution!.IsCompleted, TimeSpan.FromSeconds(5)), "The transaction worker must finish after the database lock is released.");
                    AssertThrows<OperationCanceledException>(() => execution!.GetAwaiter().GetResult(), "The queued transaction must observe cancellation without acquiring the initialization lock.");
                    Assert(queued.IsCompleted && !queued.IsCommitted, "The queued cancelled transaction must publish its completion.");
                }
            }
            AssertThrows<NullReferenceException>(() => factory.CreateNewTransaction(), "The factory must reject a missing active database after disposal.");
            AssertThrows<NullReferenceException>(() => factory.ExecuteTransactionAsync(new DatabaseTransaction()), "Submitting after disposal must reject a missing active database.");
        }

        /// <summary>Ελέγχει ότι το disposal περιμένει το κοινό lock και ότι οι επόμενες λειτουργίες απορρίπτονται.</summary>
        /// <exception cref="Exception">Αποτυγχάνει ο συγχρονισμός ή έλεγχος αποδέσμευσης.</exception>
        private void CheckDatabaseDisposal()
        {
            using (ModelDatabase database = new ModelDatabase(CreateLanguages()))
            using (ManualResetEventSlim entered = new ManualResetEventSlim())
            using (ManualResetEventSlim release = new ManualResetEventSlim())
            using (ManualResetEventSlim disposalStarted = new ManualResetEventSlim())
            {
                Task operation = Task.Run(() => database.RunUnderLock(() =>
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("The test did not release the database operation.");
                    }
                }));
                Task? disposal = null;
                try
                {
                    Assert(entered.Wait(TimeSpan.FromSeconds(5)), "The database operation must acquire the lock.");
                    disposal = Task.Run(() =>
                    {
                        disposalStarted.Set();
                        database.Dispose();
                    });
                    Assert(disposalStarted.Wait(TimeSpan.FromSeconds(5)), "Database disposal must start.");
                    Assert(!disposal.Wait(TimeSpan.FromMilliseconds(100)), "Database disposal must wait for operations holding its lock.");
                }
                finally
                {
                    release.Set();
                    Assert(operation.Wait(TimeSpan.FromSeconds(5)), "The database operation must finish after release.");
                    Assert(disposal == null || disposal.Wait(TimeSpan.FromSeconds(5)), "Database disposal must finish after release.");
                }
                AssertThrows<NullReferenceException>(() => database.TransactionFactory.CreateNewTransaction(), "Disposed databases must leave no active instance for transactions.");
            }
        }

        /// <summary>Προσομοιώνει καθυστερημένα callbacks άδειας RAM cache πριν και μετά το disposal, χωρίς πρόσβαση σε SQL.</summary>
        /// <exception cref="Exception">Αποτυγχάνει η κατασκευή της cache ή κάποιο callback.</exception>
        private void CheckCacheCallbacks()
        {
            Type cacheType = typeof(BlueprintDatabase).Assembly.GetType("Blueprint.Database.Sql.RAMCache.SqlEntityCache", true)!;
            MethodInfo refresh = cacheType.GetMethod("RefreshInBackground", BindingFlags.Instance | BindingFlags.NonPublic)!;
            IDisposable cache;
            using (ModelDatabase database = new ModelDatabase(CreateLanguages()))
            {
                using (BlueprintDatabaseContext context = BlueprintDatabase.ACTIVE_INSTANCE.GetContext())
                {
                    cache = (IDisposable)Activator.CreateInstance(cacheType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { context, TimeSpan.FromHours(1) }, null)!;
                }
                using (cache)
                {
                    refresh.Invoke(cache, new object?[] { null });
                }
                refresh.Invoke(cache, new object?[] { null });
                Assert(database.TransactionFactory.CreateNewTransaction() != null, "A callback from a disposed cache must preserve the active database.");
            }
            refresh.Invoke(cache, new object?[] { null });
            Assert(BlueprintDatabase.ACTIVE_INSTANCE == null, "A callback after database disposal must leave the active instance empty.");
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
        /// <exception cref="TimeoutException">Οι ταυτόχρονοι constructors δεν φτάνουν στο σημείο συγχρονισμού.</exception>
        private static ModelDatabase? TryCreateDatabase(Barrier barrier)
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Concurrent database construction did not reach the rendezvous.");
            }
            try
            {
                return new ModelDatabase(CreateLanguages());
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

            /// <summary>Ελέγχει την ενεργοποίηση από τον base constructor και αρχικοποιεί τον provider με προαιρετική αποτυχία.</summary>
            /// <param name="languages">Οι γλώσσες που αντιγράφονται στη βάση.</param>
            /// <param name="failInitialization">Αν πρέπει να αποτύχει η αρχικοποίηση του schema.</param>
            /// <exception cref="Exception">Αποτυγχάνει η αρχικοποίηση του provider ή η ταυτόχρονη ενεργοποίηση.</exception>
            public ModelDatabase(List<BlueprintLanguage> languages, bool failInitialization = false) : base("LifecycleModel", "Server=unused;Database=LifecycleModel", languages)
            {
                fFailInitialization = failInitialization;
                try
                {
                    BlueprintDatabase? activeDatabase = BlueprintDatabase.ACTIVE_INSTANCE;
                    if (!ReferenceEquals(activeDatabase, this))
                    {
                        throw new InvalidOperationException("The base constructor must activate this instance before provider initialization.");
                    }
                    SynchronizeEntities();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            /// <summary>Εκτελεί έναν έλεγχο κρατώντας το lock αρχικοποίησης και disposal της βάσης.</summary>
            /// <param name="action">Ο έλεγχος που θα εκτελεστεί με αποκλειστική πρόσβαση.</param>
            /// <exception cref="Exception">Αποτυγχάνει ο δοσμένος έλεγχος.</exception>
            internal void RunUnderLock(Action action)
            {
                lock (fSyncRoot)
                {
                    action();
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
            protected override void SynchronizeEntities()
            {
                InitializeContext(new DbContextOptionsBuilder().UseSqlServer(ConnectionString), context =>
                {
                    if (fFailInitialization)
                    {
                        throw new InvalidOperationException("Simulated provider failure.");
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
