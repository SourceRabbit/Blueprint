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
    /// <summary>A RAM-cached hierarchy with explicit physical column names.</summary>
    [Entity("hierarchy_nodes")]
    public abstract class HierarchyNode : BlueprintEntityRAM
    {
        [Column("parent_key")]
        public abstract long? ParentID { get; set; }

        [ForeignKey(nameof(ParentID))]
        public abstract HierarchyNode? Parent { get; set; }

        [Column("node_depth"), DefaultValue(0), DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public abstract int Level { get; }

        public abstract string? Label { get; set; }

        /// <summary>Stores the depth of each node in the tree.</summary>
        [EntityTriggers]
        public static void ConfigureTriggers(EntityTriggerBuilder<HierarchyNode> triggers)
        {
            triggers.MaintainHierarchyLevel("TR_Nodes_Level", node => node.ParentID, node => node.Level);
        }
    }

    /// <summary>Checks SQL and Entity API writes, hierarchy validation, schema reconciliation and RAM reads.</summary>
    internal sealed class HierarchyLevelTriggerTest : BlueprintTest
    {
        public override void RunTest()
        {
            string name = "BlueprintHierarchyTest_" + Guid.NewGuid().ToString("N");
            string master = new SqlConnectionStringBuilder(fDatabaseConnectionString) { InitialCatalog = "master" }.ConnectionString;
            string connection = new SqlConnectionStringBuilder(fDatabaseConnectionString) { InitialCatalog = name }.ConnectionString;
            List<BlueprintLanguage> languages = new List<BlueprintLanguage> { new BlueprintLanguage(1, string.Empty) };
            SqlTestHelper.Execute(master, $"CREATE DATABASE [{name}]");
            try
            {
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(HierarchyNode));
                    HierarchyNode root = HierarchyNode.Create();
                    HierarchyNode.Insert(root);
                    HierarchyNode branch = HierarchyNode.Create();
                    branch.ParentID = root.ID;
                    HierarchyNode.Insert(branch);
                    HierarchyNode leaf = HierarchyNode.Create();
                    leaf.ParentID = branch.ID;
                    HierarchyNode.Insert(leaf);
                    Assert(root.Level == 0 && branch.Level == 1 && leaf.Level == 2, "Inserts must return the database-generated depths.");
                    Assert(HierarchyNode.SingleOrDefault(row => row.ID == leaf.ID)!.Level == 2, "The RAM read must see the original depth.");
                    branch.ParentID = null;
                    HierarchyNode.Update(branch);
                    Assert(branch.Level == 0 && HierarchyNode.SingleOrDefault(row => row.ID == leaf.ID)!.Level == 1, "Promoting a node must refresh its own level and its cached descendants.");

                    SqlTestHelper.Execute(connection, """
                        INSERT dbo.hierarchy_nodes (ID, parent_key) VALUES (104,103),(101,100),(105,NULL),(102,101),(100,NULL),(103,102);
                        """);
                    Assert(Depth(connection, 104) == 4 && Depth(connection, 105) == 0, "A multi-row insert may include its own parent rows in any order.");
                    SqlTestHelper.Execute(connection, """
                        UPDATE dbo.hierarchy_nodes SET parent_key = CASE ID WHEN 100 THEN 105 WHEN 103 THEN 101 END WHERE ID IN (100,103);
                        """);
                    Assert(Depth(connection, 100) == 1 && Depth(connection, 102) == 3 && Depth(connection, 104) == 4, "Overlapping moves must use the final tree and update each descendant once.");
                    SqlTestHelper.Execute(connection, "BEGIN TRANSACTION; UPDATE dbo.hierarchy_nodes SET parent_key=NULL WHERE ID=101; ROLLBACK TRANSACTION;");
                    Assert(Depth(connection, 101) == 2 && Depth(connection, 104) == 4, "Rolling back the move must also roll back all descendant updates.");
                    try
                    {
                        SqlTestHelper.Execute(connection, "UPDATE dbo.hierarchy_nodes SET parent_key=104 WHERE ID=100;");
                        throw new InvalidOperationException("A move under a descendant must fail.");
                    }
                    catch (SqlException exception) when (exception.Number == 51001)
                    {
                        Assert(Depth(connection, 100) == 1, "An invalid move must leave the original hierarchy intact.");
                    }
                    SqlTestHelper.Execute(connection, "UPDATE dbo.hierarchy_nodes SET node_depth=99 WHERE ID=104; UPDATE dbo.hierarchy_nodes SET Label=N'edited' WHERE ID=104;");
                    Assert(Depth(connection, 104) == 4, "The stored level must survive direct edits and unrelated column updates.");
                    SqlTestHelper.Execute(connection, "DISABLE TRIGGER dbo.TR_Nodes_Level ON dbo.hierarchy_nodes; UPDATE dbo.hierarchy_nodes SET node_depth=0;");
                }
                using (SqlDatabase database = new SqlDatabase(name, connection, languages))
                {
                    database.RegisterEntities(typeof(HierarchyNode));
                    Assert(Depth(connection, 104) == 4 && HierarchyNode.SingleOrDefault(row => row.ID == 104)!.Level == 4, "Registration must repair and populate the hierarchy before loading RAM.");
                }
                Console.WriteLine($"Hierarchy level tests passed: {getAssertionCount()} assertions.");
            }
            finally
            {
                SqlConnection.ClearAllPools();
                SqlTestHelper.Execute(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
            }
        }

        /// <summary>Reads a stored depth independently of the RAM cache.</summary>
        private static long Depth(string connection, long id)
        {
            return SqlTestHelper.Scalar(connection, $"SELECT node_depth FROM dbo.hierarchy_nodes WHERE ID={id}");
        }
    }
}
