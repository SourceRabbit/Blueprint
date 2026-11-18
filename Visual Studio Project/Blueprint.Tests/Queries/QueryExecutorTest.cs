// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database;
using Blueprint.Database.Queries;
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Fundamentals;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Data.Common;
using System.Reflection;

namespace Blueprint.Tests.Queries
{
    /// <summary>Ελέγχει τη σύνθεση και SQL μετάφραση queries μέσω του δημόσιου API χωρίς σύνδεση σε SQL Server.</summary>
    internal sealed class QueryExecutorTest : BlueprintTest
    {
        /// <summary>Επαληθεύει φίλτρα, projections, γλώσσες και επανεκτέλεση των visitors στο ίδιο query.</summary>
        /// <exception cref="Exception">Αποτυγχάνει η δημιουργία του μοντέλου, η μετάφραση ή κάποιος έλεγχος.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            IQueryable<ExecutorProduct> deferred;
            using (QueryModelDatabase database = new QueryModelDatabase())
            {
                database.RegisterEntity<ExecutorProduct>();
                deferred = ExecutorProduct.Where(product => product.Price == 10m);
                string priceSql = deferred.ToQueryString();
                Assert(priceSql.Contains("QueryExecutorProducts") && priceSql.Contains("[Price] = 10"), "Entity filters must reach the SQL provider through the original query root.");
                string pageSql = deferred.OrderBy(product => product.ID).Skip(1).Take(2).ToQueryString();
                Assert(pageSql.Contains("ORDER BY") && pageSql.Contains("OFFSET"), "Ordering and paging must remain part of the translated query.");
                string projectionSql = deferred.Select(product => product.Price).ToQueryString();
                Assert(projectionSql.Contains("[Price]") && !projectionSql.Contains("[Title]"), "Scalar projections must preserve the requested fields.");
                IQueryable untyped = deferred.Provider.CreateQuery(deferred.Expression);
                Assert(untyped.ToQueryString() == priceSql, "Non-generic query composition must retain the same provider and expression.");
                IQueryable<decimal> prices = deferred.Select(product => product.Price);
                Assert(prices.Provider.CreateQuery(prices.Expression).ToQueryString() == projectionSql, "Non-generic composition must also support scalar projections.");

                string search = "φορεμα";
                CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
                IQueryable<string?> translated = ExecutorProduct.Where(product => product.Title.Contains(search, options), 2)
                    .OrderByDescending(product => product.Title.StartsWith(search, options))
                    .ThenBy(product => product.ID)
                    .Select(product => product.Title);
                string translatedSql = translated.ToQueryString();
                Assert(translatedSql.Contains("[TitleFR]") && translatedSql.Contains("DATALENGTH"), "Multilingual fallback must be applied before text search translation.");
                Assert(translatedSql.Contains("Latin1_General_100_CI_AI_SC") && translatedSql.Contains("ORDER BY"), "Contains and StartsWith ranking must use the selected comparison options.");

                options = CompareOptions.IgnoreCase;
                string repeatedSql = translated.ToQueryString();
                Assert(repeatedSql.Contains("Latin1_General_100_CI_AS_SC") && !repeatedSql.Contains("Latin1_General_100_CI_AI_SC"), "Reusing a query must reevaluate captured comparison options.");
                options = CompareOptions.IgnoreSymbols;
                AssertThrows<ArgumentOutOfRangeException>(() => translated.ToQueryString(), "Unsupported comparison options must propagate their original exception.");
                options = CompareOptions.None;
                Assert(translated.ToQueryString().Contains("Latin1_General_100_CS_AS_SC"), "A failed translation must not poison later executions of the same query.");
                Assert(!ExecutorProduct.Where(product => product.Title == "Default").Select(product => product.Title).ToQueryString().Contains("[TitleFR]"), "Default-language filters and projections must remain independent of earlier translated queries.");
                AssertThrows<ArgumentOutOfRangeException>(() => ExecutorProduct.Where(product => product.Price == 10m, 999).ToQueryString(), "Query routing must retain language validation.");
                CheckCommands();
                CheckConcurrentEntityQueries();
                database.RegisterEntity<ExecutorCategory>();
                Assert(deferred.ToQueryString() == priceSql, "A deferred query must resolve the current context after registering another entity.");
                Assert(translated.ToQueryString().Contains("[TitleFR]"), "Recreated contexts must preserve the language of existing queries.");
            }
            AssertThrows<NullReferenceException>(() => deferred.ToList(), "Deferred queries must reject a missing active database.");
            AssertThrows<NullReferenceException>(() => ((System.Collections.IEnumerable)deferred).GetEnumerator(), "Non-generic enumeration must reject a missing active database.");
            using (QueryModelDatabase replacement = new QueryModelDatabase())
            {
                replacement.RegisterEntity<ExecutorProduct>();
                Assert(deferred.ToQueryString().Contains("[Price] = 10"), "Deferred queries must resolve the currently active database at execution time.");
            }
            Console.WriteLine($"Query executor translation checks passed: {getAssertionCount()} assertions.");
        }

        /// <summary>Ελέγχει αν δύο Entity queries μπορούν να μπουν ταυτόχρονα στη μετάφραση SQL, κρατώντας το πρώτο μέσα στην αξιολόγηση παραμέτρων.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος ή ο συγχρονισμός των δύο threads.</exception>
        private void CheckConcurrentEntityQueries()
        {
            using (ManualResetEventSlim firstEntered = new ManualResetEventSlim())
            using (ManualResetEventSlim releaseFirst = new ManualResetEventSlim())
            using (ManualResetEventSlim secondStarted = new ManualResetEventSlim())
            using (ManualResetEventSlim secondEntered = new ManualResetEventSlim())
            {
                ThreadLocal<BlueprintDatabaseContext?> executingContext = (ThreadLocal<BlueprintDatabaseContext?>)typeof(BlueprintDatabase).GetField("fExecutingContext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(BlueprintDatabase.ACTIVE_INSTANCE)!;
                BlueprintDatabaseContext? firstContext = null;
                BlueprintDatabaseContext? secondContext = null;
                bool nestedContextPreserved = false;
                bool firstContextCleared = false;
                bool secondContextCleared = false;
                Func<decimal> firstPrice = () =>
                {
                    firstContext = executingContext.Value;
                    _ = ExecutorProduct.Create();
                    nestedContextPreserved = ReferenceEquals(firstContext, executingContext.Value);
                    firstEntered.Set();
                    if (!releaseFirst.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("The test did not release the first Entity query.");
                    }
                    return 10m;
                };
                Func<decimal> secondPrice = () =>
                {
                    secondContext = executingContext.Value;
                    secondEntered.Set();
                    return 20m;
                };
                IQueryable<ExecutorProduct> firstQuery = ExecutorProduct.Where(product => product.Price == firstPrice());
                IQueryable<ExecutorProduct> secondQuery = ExecutorProduct.Where(product => product.Price == secondPrice());
                Task<string> first = Task.Factory.StartNew(() =>
                {
                    string sql = firstQuery.ToQueryString();
                    firstContextCleared = executingContext.Value == null;
                    return sql;
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Task<string>? second = null;
                try
                {
                    Assert(firstEntered.Wait(TimeSpan.FromSeconds(5)), "The first Entity query must enter parameter evaluation.");
                    second = Task.Factory.StartNew(() =>
                    {
                        secondStarted.Set();
                        string sql = secondQuery.ToQueryString();
                        secondContextCleared = executingContext.Value == null;
                        return sql;
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    Assert(secondStarted.Wait(TimeSpan.FromSeconds(5)), "The second thread must start its Entity query.");
                    Assert(secondEntered.Wait(TimeSpan.FromSeconds(5)), "The second Entity query must enter while the first is still running.");
                    Assert(second.Wait(TimeSpan.FromSeconds(5)) && !first.IsCompleted, "The second Entity query must finish before the first is released.");
                    Assert(firstContext != null && secondContext != null && !ReferenceEquals(firstContext, secondContext), "Concurrent operations must reserve distinct contexts.");
                    Assert(nestedContextPreserved, "Nested Entity calls must preserve the outer operation's context.");
                }
                finally
                {
                    releaseFirst.Set();
                    Assert(first.Wait(TimeSpan.FromSeconds(5)), "The first Entity query must finish after release.");
                    Assert(second == null || second.Wait(TimeSpan.FromSeconds(5)), "The second Entity query must finish.");
                }
                Assert(firstContextCleared && secondContextCleared, "Each operation must clear its thread's context before returning.");
                Assert(first.Result.Contains("QueryExecutorProducts") && second!.Result.Contains("QueryExecutorProducts"), "Both Entity queries must produce SQL for the expected table.");
                Func<decimal> failingPrice = () => throw new InvalidOperationException("Expected parameter evaluation failure.");
                AssertThrows<InvalidOperationException>(() => ExecutorProduct.Where(product => product.Price == failingPrice()).ToQueryString(), "A failed query must propagate its parameter evaluation error.");
                Assert(executingContext.Value == null, "A failed operation must also clear its thread's context.");
                Console.WriteLine("Entity API concurrency check: the second query completed while the first was blocked, using a different context.");
            }
        }

        /// <summary>Ελέγχει validation και κενά batches του Entity API χωρίς σύνδεση SQL.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος ή επιχειρείται μη αναμενόμενη πρόσβαση στη βάση.</exception>
        private void CheckCommands()
        {
            ExecutorProduct entity = ExecutorProduct.Create();
            Assert(entity.ID == 0 && entity.CurrentLanguage == 7, "Entity creation must preserve default IDs and language after removing collections.");
            entity.ID = 42;
            AssertThrows<ArgumentException>(() => ExecutorProduct.Insert(entity), "Insert must reject assigned IDs before accessing SQL.");
            AssertThrows<ArgumentException>(() => ExecutorProduct.Insert(new ExecutorProduct[] { null! }), "Insert must validate every batch item before persistence.");
            AssertThrows<ArgumentException>(() => ExecutorProduct.Update(new ExecutorProduct[] { null! }), "Update must validate every batch item before persistence.");
            Assert(ExecutorProduct.Insert(Array.Empty<ExecutorProduct>()) == 0, "An empty insert must remain a successful zero-row write.");
            Assert(ExecutorProduct.Update(Array.Empty<ExecutorProduct>()) == 0, "An empty update must remain a successful zero-row write.");
            AssertThrows<ArgumentNullException>(() => ExecutorProduct.Delete(null!), "Delete must reject a null predicate before accessing SQL.");
        }

        /// <summary>Δημιουργεί μοντέλο EF για ελέγχους μετάφρασης χωρίς πρόσβαση σε πραγματικά δεδομένα.</summary>
        private sealed class QueryModelDatabase : BlueprintDatabase
        {
            /// <summary>Αρχικοποιεί τον provider με προεπιλεγμένη και δεύτερη γλώσσα χωρίς σύνδεση SQL.</summary>
            /// <exception cref="Exception">Αποτυγχάνει η αρχικοποίηση του μοντέλου.</exception>
            internal QueryModelDatabase() : base("QueryExecutorModel", "Server=unused;Database=QueryExecutorModel", new List<BlueprintLanguage> { new BlueprintLanguage(7, string.Empty), new BlueprintLanguage(2, "FR") })
            {
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

            /// <summary>Κατασκευάζει το μοντέλο του SQL provider παραλείποντας δημιουργία βάσης και συγχρονισμό schema.</summary>
            /// <exception cref="Exception">Αποτυγχάνει η δημιουργία του μοντέλου entities.</exception>
            protected override void SynchronizeEntities()
            {
                InitializeContext(new DbContextOptionsBuilder().UseSqlServer(ConnectionString), context =>
                {
                });
            }
        }
    }

    /// <summary>Ορίζει αριθμητικό φίλτρο και πολύγλωσσο κείμενο για τους ελέγχους σύνθεσης queries.</summary>
    [Entity("QueryExecutorProducts")]
    public abstract class ExecutorProduct : BlueprintEntityDB
    {
        [Precision(18, 2)]
        public abstract decimal Price { get; set; }

        [Multilingual, FallbackToDefaultLanguage]
        public abstract string? Title { get; set; }
    }

    /// <summary>Προκαλεί αναδημιουργία του μοντέλου για έλεγχο των ήδη συντεθειμένων queries.</summary>
    [Entity("QueryExecutorCategories")]
    public abstract class ExecutorCategory : BlueprintEntityDB
    {
        public abstract string? Name { get; set; }
    }
}
