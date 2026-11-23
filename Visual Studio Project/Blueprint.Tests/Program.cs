// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Tests.Tutorial1;

namespace Blueprint.Tests
{
    /// <summary>Creates and runs Blueprint tests through the shared RunTest method.</summary>
    internal static class Program
    {
        /// <summary>Runs isolated feature checks, including loopback email submission with --smtp-email, or the configured performance test.</summary>
        /// <remarks>Use --context-pool-insert &lt;poolSize&gt; [server] [productCount] to insert a Product list using one worker thread per pool slot in a temporary SQL database.</remarks>
        /// <param name="args">Use --entity-classes for model/query/lifecycle checks, --entity-classes-sql for class persistence, --multilingual-fallback for translations, --sql-execute-non-query for standalone SQL commands, --sql-log for logging, --ram-cache-auto-include for cached relationships, or --ram-cache-change-tracking for synchronization; otherwise run the configured performance test.</param>
        /// <returns>Zero on success or one on failure.</returns>
        private static int Main(string[] args)
        {
            if (args.Contains("--smtp-email"))
            {
                Email.SMTPEmailSenderTest test = new Email.SMTPEmailSenderTest();
                test.RunTest();
                return 0;
            }

            if (args.Length >= 2 && args[0] == "--context-pool-insert")
            {
                Database.ContextPoolInsertTest poolTest = new Database.ContextPoolInsertTest(int.Parse(args[1]), args.Length >= 3 ? args[2] : null, args.Length >= 4 ? int.Parse(args[3]) : 1);
                poolTest.RunTest();
                return 0;
            }

            if (args.Contains("--entity-values"))
            {
                Entities.Triggers.ValueTriggerTest valueTest = new Entities.Triggers.ValueTriggerTest();
                valueTest.RunTest();
                return 0;
            }

            if (args.Contains("--entity-triggers"))
            {
                Entities.Triggers.CountTriggerTest triggerTest = new Entities.Triggers.CountTriggerTest();
                triggerTest.RunTest();
                return 0;
            }

            if (args.Contains("--entity-classes-sql"))
            {
                Entities.EntityClassPersistenceTest persistenceTest = new Entities.EntityClassPersistenceTest();
                persistenceTest.RunTest();
                return 0;
            }

            if (args.Contains("--entity-classes"))
            {
                Entities.EntityClassTest entityTest = new Entities.EntityClassTest();
                entityTest.RunTest();
                Queries.QueryExecutorTest queryTest = new Queries.QueryExecutorTest();
                queryTest.RunTest();
                Lifecycle.LifecycleTest lifecycleTest = new Lifecycle.LifecycleTest(false);
                lifecycleTest.RunTest();
                return 0;
            }

            if (args.Contains("--multilingual-fallback"))
            {
                Localization.MultilingualFallbackTest fallbackTest = new Localization.MultilingualFallbackTest();
                fallbackTest.RunTest();
                return 0;
            }

            if (args.Contains("--sql-execute-non-query"))
            {
                Sql.ExecuteNonQueryTest commandTest = new Sql.ExecuteNonQueryTest();
                commandTest.RunTest();
                return 0;
            }

            if (args.Contains("--sql-log"))
            {
                Log.SqlLogTest logTest = new Log.SqlLogTest();
                logTest.RunTest();
                return 0;
            }

            if (args.Contains("--ram-cache-auto-include"))
            {
                RAMCache.AutoIncludeTest autoIncludeTest = new RAMCache.AutoIncludeTest();
                autoIncludeTest.RunTest();
                return 0;
            }
            if (args.Contains("--ram-cache-change-tracking"))
            {
                RAMCache.ChangeTrackingTest changeTrackingTest = new RAMCache.ChangeTrackingTest();
                changeTrackingTest.RunTest();
                return 0;
            }

            Performance.RAMCachePerformanceTest performanceTest = new Performance.RAMCachePerformanceTest();
            performanceTest.RunTest();


            /*
                Queries.QueryExecutorTest queryExecutorTest = new Queries.QueryExecutorTest();
                queryExecutorTest.RunTest();
            */

            /*
                Localization.MultilingualFallbackTest fallbackTest = new Localization.MultilingualFallbackTest();
                fallbackTest.RunTest();
            */


            /*
          
                Lifecycle.LifecycleTest lifecycleTest = new Lifecycle.LifecycleTest(args.Contains("--lifecycle-sql"));
                lifecycleTest.RunTest();
                return 0;
            */



            /*
            PerformanceTest.PerformanceTest performanceTest = new PerformanceTest.PerformanceTest();
            performanceTest.RunTest();
            */


            /*
            EntitiesTutorial tutorial = new EntitiesTutorial();
            tutorial.RunTest();
            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
            */
            return 0;



        }

    }
}
