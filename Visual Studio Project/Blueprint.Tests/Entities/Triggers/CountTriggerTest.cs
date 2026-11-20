// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database.Sql;
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Entities.Triggers;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Blueprint.Tests.Entities.Triggers
{
    /// <summary>A target with an explicitly mapped database-owned counter.</summary>
    [Entity("owners")]
    public abstract class Owner : BlueprintEntityRAM
    {
        public abstract string? Label { get; set; }

        [Column("item_count"), DefaultValue(0), DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public abstract int ItemCount { get; }
    }

    /// <summary>A source with an optional owner and a conditional count.</summary>
    [Entity("items")]
    public abstract class Item : BlueprintEntityDB
    {
        [Column("owner_id")]
        public abstract long? OwnerID { get; set; }

        [ForeignKey(nameof(OwnerID))]
        public abstract Owner? Owner { get; set; }

        public abstract bool IsListed { get; set; }

        [DefaultValue((byte)0)]
        public abstract byte Tier { get; set; }

        /// <summary>Counts listed items against their assigned owner.</summary>
        /// <param name="triggers">The source entity trigger builder.</param>
        [EntityTriggers]
        public static void ConfigureTriggers(EntityTriggerBuilder<Item> triggers)
        {
            triggers.MaintainCount<Owner>("TR_Items_OwnerCount", item => item.OwnerID, owner => owner.ItemCount, item => item.IsListed && item.Tier < 2);
        }
    }

    /// <summary>Exercises count triggers through entity writes, independent SQL clients and repeated registration.</summary>
    internal sealed class CountTriggerTest : BlueprintTest
    {
        /// <summary>Checks SQL-generated IDs, set-based deltas, rollback, repair and read-only counter persistence.</summary>
        public override void RunTest()
        {
            string name = "BlueprintCountTrigger_" + Guid.NewGuid().ToString("N");
            string server = Environment.GetEnvironmentVariable("BLUEPRINT_TEST_SQL_CONNECTION_STRING") ?? fDatabaseConnectionString;
            string master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master", Pooling = false }.ConnectionString;
            string connection = new SqlConnectionStringBuilder(server) { InitialCatalog = name, Pooling = false }.ConnectionString;
            List<BlueprintLanguage> languages = new List<BlueprintLanguage> { new BlueprintLanguage(1, string.Empty) };
            SqlTestHelper.Execute(master, $"CREATE DATABASE [{name}]");
            try
            {
                long ownerID;
                long otherID;
                long triggerID;
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(Item));
                    Owner owner = Owner.Create();
                    Assert(typeof(Owner).GetProperty(nameof(Owner.ItemCount))!.SetMethod == null && owner.GetType().GetProperty(nameof(Owner.ItemCount))!.GetSetMethod() == null, "The counter must expose only a public getter on both the contract and the generated entity.");
                    Owner other = Owner.Create();
                    Owner.Insert(new[] { owner, other });
                    ownerID = owner.ID;
                    otherID = other.ID;
                    Item item = Item.Create();
                    item.IsListed = true;
                    item.OwnerID = ownerID;
                    Item.Insert(item);
                    Assert(item.ID > 0 && ReadCount(connection, ownerID) == 1, "Entity insert must return its generated key and count the item.");
                    owner.Label = "Edited with a stale count";
                    Owner.Update(owner);
                    Assert(owner.ItemCount == 1 && ReadCount(connection, ownerID) == 1, "Editing the owner must preserve and reload the database counter.");
                    Owner cached = Owner.Where(row => row.ItemCount > 0).OrderByDescending(row => row.ItemCount).First();
                    Assert(cached.ID == ownerID && cached.ItemCount == 1, "The RAM cache must copy and query a getter-only counter.");
                    Assert(Owner.FromJson(cached.ToJson()).ItemCount == 1, "A JSON snapshot must round-trip a getter-only counter.");
                    item.IsListed = false;
                    Item.Update(item);
                    Assert(ReadCount(connection, ownerID) == 0, "An item leaving the filter must be removed from the count.");
                    SqlTestHelper.Execute(connection, $"INSERT dbo.items(owner_id, IsListed) VALUES ({ownerID},1),({ownerID},1),(NULL,1),({otherID},0)");
                    Assert(ReadCount(connection, ownerID) == 2 && ReadCount(connection, otherID) == 0, "Multi-row inserts must exclude NULL keys and unlisted rows.");
                    SqlTestHelper.Execute(connection, $"UPDATE dbo.items SET owner_id={otherID} WHERE owner_id={ownerID}");
                    Assert(ReadCount(connection, ownerID) == 0 && ReadCount(connection, otherID) == 2, "Moving multiple items must update both owners.");
                    SqlTestHelper.Execute(connection, "BEGIN TRANSACTION; DELETE FROM dbo.items; ROLLBACK TRANSACTION;");
                    Assert(ReadCount(connection, otherID) == 2, "Rolling back a delete must roll back the counter.");
                    Item.Delete(row => row.IsListed);
                    Assert(ReadCount(connection, otherID) == 0, "Entity deletion must update the count.");
                    SqlTestHelper.Execute(connection, "DROP TRIGGER dbo.TR_Items_OwnerCount");
                    // Simulate an older production schema that has rows but no counter column.
                    SqlTestHelper.Execute(connection, """
                        DECLARE @constraint sysname = (
                            SELECT d.name FROM sys.default_constraints AS d JOIN sys.columns AS c
                              ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
                            WHERE c.object_id = OBJECT_ID(N'dbo.owners') AND c.name = N'item_count'
                        );
                        DECLARE @ddl nvarchar(max) = N'ALTER TABLE dbo.owners DROP CONSTRAINT ' + QUOTENAME(@constraint);
                        EXEC sys.sp_executesql @ddl;
                        ALTER TABLE dbo.owners DROP COLUMN item_count;
                        """);
                    SqlTestHelper.Execute(connection, $"INSERT dbo.items(owner_id, IsListed) VALUES ({ownerID},1),({ownerID},1)");
                }
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(Item));
                    Assert(ReadCount(connection, ownerID) == 2, "Installing a missing trigger must backfill existing rows.");
                    triggerID = SqlTestHelper.Scalar(connection, "SELECT OBJECT_ID(N'dbo.TR_Items_OwnerCount')");
                }
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(Item));
                    Assert(triggerID == SqlTestHelper.Scalar(connection, "SELECT OBJECT_ID(N'dbo.TR_Items_OwnerCount')") && ReadCount(connection, ownerID) == 2, "An unchanged trigger must be retained without double counting.");
                }
                SqlTestHelper.Execute(connection, $"DISABLE TRIGGER dbo.TR_Items_OwnerCount ON dbo.items; UPDATE dbo.items SET Tier=2 WHERE owner_id={ownerID}");
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(Item));
                    Assert(ReadCount(connection, ownerID) == 0, "Repairing a disabled trigger must recount using the same filter.");
                    SqlTestHelper.Execute(connection, $"UPDATE dbo.items SET Tier=0 WHERE owner_id={ownerID}");
                    Assert(ReadCount(connection, ownerID) == 2, "Repair must re-enable the trigger.");
                }
                Console.WriteLine($"Entity trigger tests passed: {getAssertionCount()} assertions.");
            }
            finally
            {
                SqlConnection.ClearAllPools();
                SqlTestHelper.Execute(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
            }
        }

        /// <summary>Reads the persisted count independently of EF and RAM caching.</summary>
        /// <param name="connection">The temporary database connection.</param>
        /// <param name="id">The owner ID.</param>
        /// <returns>The count stored by the server.</returns>
        private static long ReadCount(string connection, long id)
        {
            return SqlTestHelper.Scalar(connection, $"SELECT item_count FROM dbo.owners WHERE ID={id}");
        }
    }
}
