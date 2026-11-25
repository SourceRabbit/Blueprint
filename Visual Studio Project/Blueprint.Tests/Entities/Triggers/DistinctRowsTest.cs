// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database.Sql;
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Entities.Triggers;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Blueprint.Tests.Entities.Triggers
{
    /// <summary>Source rows shared by the key-only and parent/child projections.</summary>
    [Entity("distinct_source")]
    public abstract class DistinctSource : BlueprintEntityDB
    {
        [Column("group_id")]
        public abstract long GroupID { get; set; }
        public abstract bool Active { get; set; }
        public abstract string? Label { get; set; }
    }

    /// <summary>One parent for each group that has at least one active source.</summary>
    [Entity("distinct_parent"), EntityIndex("UX_DistinctParent_Group", nameof(GroupID), IsUnique = true)]
    public abstract class DistinctParent : BlueprintEntityDB
    {
        [Column("bucket_id")]
        public abstract long GroupID { get; set; }

        [EntityTriggers]
        public static void ConfigureTriggers(EntityTriggerBuilder<DistinctParent> triggers)
        {
            triggers.MaintainDistinctRows("Sync_ActiveGroups", p => new { p.GroupID },
                    db => db.Rows<DistinctSource>().Where(s => s.Active)
                        .Select(s => new { s.GroupID, Value = s.Label == null ? null : s.Label.Trim() }))
                .WithValues<DistinctLabel>(v => v.ParentID, v => v.Label, row => row.Value)
                .OnChange<DistinctSource>("TR_Source_ActiveGroups", (_, changed) => changed.Select(s => new { s.GroupID }),
                    s => new { s.GroupID, s.Active, s.Label }, replaceExisting: true);
        }
    }

    /// <summary>Distinct nonempty labels of an active group.</summary>
    [Entity("distinct_labels"), EntityIndex("UX_DistinctLabel_Parent_Label", nameof(ParentID), nameof(Label), IsUnique = true)]
    public abstract class DistinctLabel : BlueprintEntityDB
    {
        public abstract long ParentID { get; set; }
        [ForeignKey(nameof(ParentID))]
        public abstract DistinctParent Parent { get; set; }
        [Required, MaxLength(120)]
        public abstract string Label { get; set; }
    }

    /// <summary>Key-only projection of every group, including inactive source rows.</summary>
    [Entity("distinct_all"), EntityIndex("UX_DistinctAll_Group", nameof(GroupID), IsUnique = true)]
    public abstract class DistinctGroup : BlueprintEntityDB
    {
        public abstract long GroupID { get; set; }

        [EntityTriggers]
        public static void ConfigureTriggers(EntityTriggerBuilder<DistinctGroup> triggers)
        {
            triggers.MaintainDistinctRows("Sync_AllGroups", p => new { p.GroupID },
                    db => db.Rows<DistinctSource>().Select(s => new { s.GroupID }))
                .OnChange<DistinctSource>("TR_Source_AllGroups", (_, changed) => changed.Select(s => new { s.GroupID }), s => s.GroupID);
        }
    }

    /// <summary>Verifies installation, repair, distinct values, mapped columns and writes from SQL and Blueprint.</summary>
    internal sealed class DistinctRowsTest : BlueprintTest
    {
        public override void RunTest()
        {
            string name = "BlueprintDistinctRows_" + Guid.NewGuid().ToString("N");
            string server = Environment.GetEnvironmentVariable("BLUEPRINT_TEST_SQL_CONNECTION_STRING") ?? fDatabaseConnectionString;
            string master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master", Pooling = false }.ConnectionString;
            string connection = new SqlConnectionStringBuilder(server) { InitialCatalog = name, Pooling = false }.ConnectionString;
            List<BlueprintLanguage> languages = [new BlueprintLanguage(1, string.Empty)];
            Type[] contracts = [typeof(DistinctSource), typeof(DistinctParent), typeof(DistinctLabel), typeof(DistinctGroup)];
            long Read(string sql) => SqlTestHelper.Scalar(connection, sql);
            void Write(string sql) => SqlTestHelper.Execute(connection, sql);
            SqlTestHelper.Execute(master, $"CREATE DATABASE [{name}] COLLATE Latin1_General_100_CI_AS");
            try
            {
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(DistinctSource));
                    Write("INSERT dbo.distinct_source(group_id, Active, Label) VALUES (1,1,'Red'),(1,1,' Red '),(1,1,' '),(2,0,'Blue')");
                    Write("CREATE TRIGGER dbo.TR_Source_ActiveGroups ON dbo.distinct_source AFTER INSERT AS RETURN");
                    Write("CREATE TRIGGER dbo.TR_Unrelated ON dbo.distinct_source AFTER INSERT AS RETURN");
                }
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(contracts);
                    Assert(Read("SELECT COUNT(*) FROM dbo.distinct_parent") == 1 && Read("SELECT COUNT(*) FROM dbo.distinct_labels") == 1, "Backfill must ignore empty, duplicate and inactive values.");
                    Assert(Read("SELECT COUNT(*) FROM dbo.distinct_all") == 2, "The key-only projection includes inactive groups.");
                    long redID = Read("SELECT ID FROM dbo.distinct_labels");
                    Write("UPDATE dbo.distinct_source SET Label='red' WHERE Active=1 AND LTRIM(RTRIM(Label))='Red'");
                    Assert(Read("SELECT ID FROM dbo.distinct_labels WHERE Label COLLATE Latin1_General_100_BIN2='red'") == redID, "A case-only edit must update the existing display value.");
                    DistinctSource item = DistinctSource.Create();
                    item.GroupID = 1;
                    item.Active = true;
                    item.Label = "Green";
                    DistinctSource.Insert(item);
                    Assert(item.ID > 0 && Read("SELECT COUNT(*) FROM dbo.distinct_labels") == 2, "Blueprint inserts must return their IDs and update the projection.");
                    Write("UPDATE dbo.distinct_source SET group_id=2 WHERE Label='red'");
                    Assert(Read("SELECT COUNT(*) FROM dbo.distinct_parent") == 2 && Read("SELECT COUNT(*) FROM dbo.distinct_labels") == 2, "Multi-row moves must refresh both keys.");
                    Write("BEGIN TRAN; DELETE FROM dbo.distinct_source; ROLLBACK");
                    Assert(Read("SELECT COUNT(*) FROM dbo.distinct_labels") == 2, "Source rollback must restore projected rows.");
                    Write("DELETE FROM dbo.distinct_source WHERE Active=1 AND group_id=2");
                    Assert(Read("SELECT COUNT(*) FROM dbo.distinct_parent") == 1 && Read("SELECT COUNT(*) FROM dbo.distinct_all") == 2, "The last qualifying row removes only its projection.");
                    Write("DISABLE TRIGGER dbo.TR_Source_ActiveGroups ON dbo.distinct_source; UPDATE dbo.distinct_source SET Active=0");
                }
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(contracts);
                    Assert(Read("SELECT COUNT(*) FROM dbo.distinct_parent") == 0 && Read("SELECT COUNT(*) FROM dbo.distinct_labels") == 0, "Repair must backfill and remove stale parent and child rows.");
                }
                long proc = Read("SELECT OBJECT_ID('dbo.Sync_ActiveGroups')");
                long trigger = Read("SELECT OBJECT_ID('dbo.TR_Source_ActiveGroups')");
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(contracts);
                    Assert(Read("SELECT OBJECT_ID('dbo.Sync_ActiveGroups')") == proc && Read("SELECT OBJECT_ID('dbo.TR_Source_ActiveGroups')") == trigger, "Re-registration must retain unchanged objects.");
                    Assert(Read("SELECT OBJECT_ID('dbo.TR_Unrelated')") > 0, "Explicit adoption must preserve unrelated triggers.");
                }
                Write("ALTER PROCEDURE dbo.Sync_ActiveGroups AS RETURN");
                Write("UPDATE dbo.distinct_source SET Active=1");
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(contracts);
                    Assert(Read("SELECT COUNT(*) FROM dbo.distinct_parent") == 2, "Replacing only the synchronization procedure must also backfill.");
                    Assert(Read("SELECT OBJECT_ID('dbo.TR_Source_ActiveGroups')") == trigger, "Repairing the procedure must preserve unchanged source triggers.");
                }
                Console.WriteLine($"Distinct row tests passed: {getAssertionCount()} assertions.");
            }
            finally
            {
                SqlConnection.ClearAllPools();
                SqlTestHelper.Execute(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
            }
        }
    }
}
