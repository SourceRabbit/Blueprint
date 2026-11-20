// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database.Sql;
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Entities.Triggers;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Blueprint.Tests.Entities.Triggers
{
    [Entity("value_rates")]
    public abstract class ValueRate : BlueprintEntityRAM
    {
        [Precision(5, 2)]
        public abstract decimal Rate { get; set; }

        [Column("factor"), Precision(5, 4), DefaultValue(typeof(decimal), "1"), DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public abstract decimal Factor { get; }

        [EntityTriggers]
        public static void ConfigureTriggers(EntityTriggerBuilder<ValueRate> triggers)
        {
            triggers.MaintainValue("TR_Rates_Factor", rate => rate.Factor, rate => 1m + rate.Rate / 100m);
        }
    }

    [Entity("value_items")]
    public abstract class ValueItem : BlueprintEntityRAM
    {
        [Precision(18, 2)]
        public abstract decimal? Net { get; set; }

        public abstract long RateID { get; set; }

        [ForeignKey(nameof(RateID))]
        public abstract ValueRate Rate { get; set; }

        [Column("gross"), Precision(19, 2), DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public abstract decimal? Gross { get; }

        [EntityTriggers]
        public static void ConfigureTriggers(EntityTriggerBuilder<ValueItem> triggers)
        {
            triggers.MaintainValue("TR_Items_Gross", item => item.Gross, item => item.Net * item.Rate.Factor);
        }
    }

    /// <summary>Checks database-owned formulas over local and referenced inputs in an isolated SQL Server database.</summary>
    internal sealed class ValueTriggerTest : BlueprintTest
    {
        public override void RunTest()
        {
            string name = "BlueprintValueTrigger_" + Guid.NewGuid().ToString("N");
            string server = Environment.GetEnvironmentVariable("BLUEPRINT_TEST_SQL_CONNECTION_STRING") ?? fDatabaseConnectionString;
            string master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master", Pooling = false }.ConnectionString;
            string connection = new SqlConnectionStringBuilder(server) { InitialCatalog = name, Pooling = false }.ConnectionString;
            List<BlueprintLanguage> languages = new List<BlueprintLanguage> { new BlueprintLanguage(1, string.Empty) };
            SqlTestHelper.Execute(master, $"CREATE DATABASE [{name}]");
            try
            {
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(ValueItem));
                    ValueRate rate = ValueRate.Create();
                    rate.Rate = 24m;
                    ValueRate.Insert(rate);
                    Assert(rate.Factor == 1.24m, "EF must return the value written by the AFTER trigger.");
                    ValueItem item = ValueItem.Create();
                    item.RateID = rate.ID;
                    item.Net = 100m;
                    ValueItem.Insert(item);
                    Assert(item.Gross == 124m && ValueItem.Where(row => row.Gross == 124m).Single().Gross == 124m, "The generated price must materialize through EF and the RAM cache.");
                    SqlTestHelper.Execute(connection, "INSERT dbo.value_items(Net, RateID) VALUES (NULL,1),(0,1),(100,1); UPDATE dbo.value_rates SET Rate=13 WHERE ID=1");
                    Assert(SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_rates WHERE factor=1.1300") == 1 &&
                        SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_items WHERE gross=113") == 2 &&
                        SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_items WHERE gross=0 OR gross IS NULL") == 2, "Updating only the rate must refresh every affected price and preserve null and zero.");
                    SqlTestHelper.Execute(connection, "INSERT dbo.value_rates(Rate) VALUES (50); UPDATE dbo.value_items SET RateID=2, Net=0.01 WHERE Net=100");
                    Assert(SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_items WHERE gross=0.02") == 2, "Moving rows must switch the multiplier and round midpoint prices away from zero.");
                    SqlTestHelper.Execute(connection, "UPDATE dbo.value_items SET Net=-0.01 WHERE RateID=2");
                    Assert(SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_items WHERE gross=-0.02") == 2, "Negative midpoints must round away from zero.");
                    SqlTestHelper.Execute(connection, "BEGIN TRANSACTION; UPDATE dbo.value_rates SET Rate=0; ROLLBACK TRANSACTION;");
                    Assert(SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_items WHERE gross=-0.02") == 2, "Rolling back a rate change must roll back all dependent prices.");
                    // Recreate the previous schema: a writable factor with no default and no stored gross price.
                    SqlTestHelper.Execute(connection, """
                        DROP TRIGGER dbo.TR_Items_Gross, dbo.TR_Items_Gross_Rate, dbo.TR_Rates_Factor;
                        UPDATE dbo.value_rates SET factor=1;
                        ALTER TABLE dbo.value_items DROP COLUMN gross;
                        DECLARE @name sysname = (SELECT d.name FROM sys.default_constraints d JOIN sys.columns c
                            ON c.default_object_id=d.object_id WHERE c.object_id=OBJECT_ID(N'dbo.value_rates') AND c.name=N'factor');
                        DECLARE @ddl nvarchar(max) = N'ALTER TABLE dbo.value_rates DROP CONSTRAINT ' + QUOTENAME(@name);
                        EXEC sys.sp_executesql @ddl;
                        """);
                }
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(ValueItem));
                    Assert(SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_items WHERE gross=-0.02") == 2 &&
                        SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_items WHERE gross=0 OR gross IS NULL") == 2, "Installation must backfill both formulas on existing rows.");
                    SqlTestHelper.Execute(connection, "INSERT dbo.value_rates(Rate) VALUES (13.25)");
                    Assert(SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_rates WHERE factor=1.1325") == 1, "Upgrading must supply the required default and retain the full precision of fractional VAT rates.");
                }
                long triggerID = SqlTestHelper.Scalar(connection, "SELECT OBJECT_ID('dbo.TR_Items_Gross_Rate')");
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(ValueRate));
                    Assert(SqlTestHelper.Scalar(connection, "SELECT OBJECT_ID('dbo.TR_Items_Gross_Rate')") == triggerID, "Registering only a principal must preserve triggers owned by a dependent model.");
                    SqlTestHelper.Execute(connection, "UPDATE dbo.value_rates SET Rate=0 WHERE ID=2");
                    Assert(SqlTestHelper.Scalar(connection, "SELECT COUNT(*) FROM dbo.value_items WHERE gross=-0.01") == 2, "The dependency trigger must still function after partial registration.");
                }
                Console.WriteLine($"Maintained value tests passed: {getAssertionCount()} assertions.");
            }
            finally
            {
                SqlConnection.ClearAllPools();
                SqlTestHelper.Execute(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
            }
        }
    }
}
