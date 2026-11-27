// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Blueprint.Database.Sql.Triggers
{
    /// <summary>Generates a hierarchy trigger and initializes stored levels in the schema transaction.</summary>
    internal static class SqlHierarchyLevelTriggers
    {
        internal const string OWNER = "Blueprint.MaintainHierarchyLevel";

        /// <summary>Resolves the self-reference and builds set-based maintenance for the affected subtrees.</summary>
        internal static SqlEntityTriggers.Definition Build(BlueprintDatabaseContext context, ISqlGenerationHelper sql, HierarchyLevelTrigger rule)
        {
            IEntityType entity = context.Model.FindEntityType(context.EntityTypes.Types[rule.Entity])!;
            StoreObjectIdentifier table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            IProperty? parentKey = entity.FindProperty(rule.ParentID);
            if (parentKey?.ClrType != typeof(long?) || !entity.GetForeignKeys().Any(fk => fk.PrincipalEntityType == entity && fk.PrincipalKey.IsPrimaryKey() && fk.Properties.Count == 1 && fk.Properties[0] == parentKey))
            {
                throw new InvalidOperationException($"Trigger '{rule.Name}' requires a nullable self-referencing foreign key on '{rule.Entity.Name}.{rule.ParentID}'.");
            }

            string name = sql.DelimitIdentifier(table.Name, table.Schema);
            string trigger = sql.DelimitIdentifier(rule.Name, table.Schema);
            string id = sql.DelimitIdentifier(entity.FindPrimaryKey()!.Properties.Single().GetColumnName(table)!);
            string parent = sql.DelimitIdentifier(parentKey.GetColumnName(table)!);
            string level = sql.DelimitIdentifier(entity.FindProperty(rule.Level)!.GetColumnName(table)!);

            const string declareNodes = """
                DECLARE @nodes TABLE (nodeID bigint PRIMARY KEY, parentID bigint NULL, wave int NOT NULL,
                    INDEX ix_parent NONCLUSTERED (parentID), INDEX ix_wave NONCLUSTERED (wave));
                """;
            // Every affected node has exactly one path from a boundary. A cycle has no boundary.
            string calculateLevels = $"""
                DECLARE @levels TABLE (nodeID bigint PRIMARY KEY, depth int NOT NULL);
                ;WITH depths AS (
                    SELECT n.nodeID, COALESCE(p.{level} + 1, 0) AS depth
                    FROM @nodes AS n
                    LEFT JOIN {name} AS p WITH (UPDLOCK, HOLDLOCK) ON p.{id} = n.parentID
                    WHERE NOT EXISTS (SELECT 1 FROM @nodes AS insideTree WHERE insideTree.nodeID = n.parentID)
                      AND (n.parentID IS NULL OR p.{id} IS NOT NULL)
                    UNION ALL
                    SELECT child.nodeID, d.depth + 1
                    FROM depths AS d JOIN @nodes AS child ON child.parentID = d.nodeID
                )
                INSERT @levels (nodeID, depth) SELECT nodeID, depth FROM depths OPTION (MAXRECURSION 0);
                IF EXISTS (SELECT nodeID FROM @nodes EXCEPT SELECT nodeID FROM @levels)
                    THROW 51001, 'MaintainHierarchyLevel requires a rooted hierarchy without cycles.', 1;
                UPDATE t SET {level} = d.depth
                FROM {name} AS t JOIN @levels AS d ON d.nodeID = t.{id}
                WHERE t.{level} <> d.depth;
                """;

            string create = $"""
                CREATE TRIGGER {trigger} ON {name} AFTER INSERT, UPDATE
                AS
                BEGIN
                    IF ROWCOUNT_BIG() = 0 RETURN;
                    SET NOCOUNT ON;
                    IF NOT (UPDATE({parent}) OR UPDATE({level})) RETURN;
                    {declareNodes}
                    INSERT @nodes (nodeID, parentID, wave)
                    SELECT i.{id}, i.{parent}, 0 FROM inserted AS i LEFT JOIN deleted AS d ON d.{id} = i.{id}
                    WHERE d.{id} IS NULL OR EXISTS (SELECT i.{parent}, i.{level} EXCEPT SELECT d.{parent}, d.{level});
                    IF @@ROWCOUNT = 0 RETURN;

                    DECLARE @wave int = 0;
                    WHILE 1 = 1
                    BEGIN
                        INSERT @nodes (nodeID, parentID, wave)
                        SELECT c.{id}, c.{parent}, @wave + 1
                        FROM @nodes AS n JOIN {name} AS c WITH (UPDLOCK, HOLDLOCK) ON c.{parent} = n.nodeID
                        WHERE n.wave = @wave AND NOT EXISTS (SELECT 1 FROM @nodes AS seen WHERE seen.nodeID = c.{id});
                        IF @@ROWCOUNT = 0 BREAK;
                        SET @wave += 1;
                    END;
                    {calculateLevels}
                END
                """;
            string backfill = $"""
                {declareNodes}
                INSERT @nodes (nodeID, parentID, wave) SELECT {id}, {parent}, 0 FROM {name};
                {calculateLevels}
                """;
            return new SqlEntityTriggers.Definition(table.Schema!, table.Name, rule.Name, create.ReplaceLineEndings("\n"), backfill, OWNER, null);
        }
    }
}
