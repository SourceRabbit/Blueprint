// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace Blueprint.Database.Sql.Triggers
{
    /// <summary>Installs Blueprint-owned entity triggers and initializes stored values in the schema transaction.</summary>
    internal static class SqlEntityTriggers
    {
        private const string COUNT_OWNER = "Blueprint.MaintainCount";
        internal const string VALUE_OWNER = "Blueprint.MaintainValue";
        internal sealed record Definition(string Schema, string Table, string Name, string Create, string Backfill, string Owner, string? DeclaringTable);
        private sealed record Installed(string DeclaringTable, string? Create);

        /// <summary>Reconciles trigger definitions only for source tables present in this model.</summary>
        /// <param name="context">The active schema synchronization transaction.</param>
        internal static void Synchronize(BlueprintDatabaseContext context)
        {
            if (context.EntityTypes.Triggers.RequiresNestedTriggers)
            {
                SqlSchemaInitializer.ExecuteCommand(context, """
                    IF EXISTS (SELECT 1 FROM sys.configurations WHERE name = 'nested triggers' AND CONVERT(int, value_in_use) = 0)
                        THROW 51000, 'Chained MaintainValue formulas require the SQL Server nested triggers option.', 1;
                    """);
            }
            ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();
            HashSet<string> sources = context.Model.GetEntityTypes().Select(entity => sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Definition> desired = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<Definition> definitions = context.EntityTypes.Triggers.Counts.Select(rule => Build(context, sql, rule))
                .Concat(context.EntityTypes.Triggers.Values.SelectMany(rule => SqlValueTriggers.Build(context, sql, rule)));
            foreach (Definition definition in definitions)
            {
                string name = sql.DelimitIdentifier(definition.Name, definition.Schema);
                if (!desired.TryAdd(name, definition))
                {
                    throw new InvalidOperationException($"Duplicate trigger name {name}.");
                }
            }
            Dictionary<string, Installed> installed = ReadInstalled(context, sql);
            foreach ((string name, Installed existing) in installed)
            {
                if (sources.Contains(existing.DeclaringTable) && (!desired.TryGetValue(name, out Definition? definition) || existing.Create != definition.Create))
                {
                    SqlSchemaInitializer.ExecuteCommand(context, $"DROP TRIGGER {name}");
                }
            }
            List<string> backfills = new List<string>();
            foreach ((string name, Definition definition) in desired)
            {
                if (installed.TryGetValue(name, out Installed? existing) && existing.Create == definition.Create)
                {
                    continue;
                }
                // CREATE holds a schema modification lock on the source until backfill and commit finish.
                SqlSchemaInitializer.ExecuteCommand(context, definition.Create);
                object owner = (object?)definition.DeclaringTable ?? 1;
                context.Database.ExecuteSqlInterpolated($"EXEC sys.sp_addextendedproperty @name={definition.Owner}, @value={owner}, @level0type=N'SCHEMA', @level0name={definition.Schema}, @level1type=N'TABLE', @level1name={definition.Table}, @level2type=N'TRIGGER', @level2name={definition.Name}");
                backfills.Add(definition.Backfill);
            }
            foreach (string backfill in backfills.Distinct(StringComparer.Ordinal))
            {
                SqlSchemaInitializer.ExecuteCommand(context, backfill);
            }
        }

        /// <summary>Reads only explicitly owned triggers and treats disabled triggers as needing repair.</summary>
        /// <param name="context">The schema transaction.</param>
        /// <param name="sql">The SQL Server identifier helper.</param>
        /// <returns>The installed definitions keyed by their qualified names.</returns>
        private static Dictionary<string, Installed> ReadInstalled(BlueprintDatabaseContext context, ISqlGenerationHelper sql)
        {
            Dictionary<string, Installed> installed = new Dictionary<string, Installed>(StringComparer.OrdinalIgnoreCase);
            const string query = """
                SELECT s.name, t.name, tr.name, m.definition, tr.is_disabled, CASE WHEN p.name = @valueOwner THEN CONVERT(nvarchar(600), p.value) END
                FROM sys.triggers AS tr
                JOIN sys.tables AS t ON t.object_id = tr.parent_id
                JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                JOIN sys.sql_modules AS m ON m.object_id = tr.object_id
                JOIN sys.extended_properties AS p ON p.class = 1 AND p.major_id = tr.object_id AND p.minor_id = 0
                WHERE p.name IN (@owner, @valueOwner)
                """;
            using (DbCommand command = SqlSchemaInitializer.CreateCommand(context, query))
            {
                DbParameter owner = command.CreateParameter();
                owner.ParameterName = "@owner";
                owner.Value = COUNT_OWNER;
                command.Parameters.Add(owner);
                DbParameter valueOwner = command.CreateParameter();
                valueOwner.ParameterName = "@valueOwner";
                valueOwner.Value = VALUE_OWNER;
                command.Parameters.Add(valueOwner);
                using (DbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string schema = reader.GetString(0);
                        string source = sql.DelimitIdentifier(reader.GetString(1), schema);
                        string? create = reader.GetBoolean(4) || reader.IsDBNull(3) ? null : reader.GetString(3).ReplaceLineEndings("\n");
                        installed.Add(sql.DelimitIdentifier(reader.GetString(2), schema), new Installed(reader.IsDBNull(5) ? source : reader.GetString(5), create));
                    }
                }
            }
            return installed;
        }

        /// <summary>Builds set-based maintenance and initial population from EF's physical mappings.</summary>
        /// <param name="context">The context containing the finalized EF model.</param>
        /// <param name="sql">The SQL Server identifier helper.</param>
        /// <param name="rule">The entity-level declaration.</param>
        /// <returns>The trigger and its matching initial count statement.</returns>
        private static Definition Build(BlueprintDatabaseContext context, ISqlGenerationHelper sql, CountTrigger rule)
        {
            IEntityType source = context.Model.FindEntityType(context.EntityTypes.Types[rule.Source])!;
            IEntityType target = context.Model.FindEntityType(context.EntityTypes.Types[rule.Target])!;
            StoreObjectIdentifier sourceTable = StoreObjectIdentifier.Table(source.GetTableName()!, source.GetSchema());
            StoreObjectIdentifier targetTable = StoreObjectIdentifier.Table(target.GetTableName()!, target.GetSchema());
            IProperty? key = source.FindProperty(rule.ForeignKey);
            if (key == null || !source.GetForeignKeys().Any(fk => fk.PrincipalEntityType == target && fk.Properties.Count == 1 && fk.Properties[0] == key))
            {
                throw new InvalidOperationException($"Trigger '{rule.Name}' requires a mapped foreign key from '{rule.Source.Name}.{rule.ForeignKey}' to '{rule.Target.Name}'.");
            }
            string sourceName = sql.DelimitIdentifier(sourceTable.Name, sourceTable.Schema);
            string targetName = sql.DelimitIdentifier(targetTable.Name, targetTable.Schema);
            string trigger = sql.DelimitIdentifier(rule.Name, sourceTable.Schema);
            string fk = sql.DelimitIdentifier(key.GetColumnName(sourceTable)!);
            string id = sql.DelimitIdentifier(target.FindPrimaryKey()!.Properties.Single().GetColumnName(targetTable)!);
            string count = sql.DelimitIdentifier(target.FindProperty(rule.Counter)!.GetColumnName(targetTable)!);
            string filter = $"r.{fk} IS NOT NULL";
            if (rule.Include != null)
            {
                filter += " AND " + CountPredicate.Translate(rule.Include.Body, rule.Include.Parameters[0], name =>
                {
                    IProperty property = source.FindProperty(name) ?? throw new InvalidOperationException($"Trigger '{rule.Name}' requires a stored column for '{name}'.");
                    return "r." + sql.DelimitIdentifier(property.GetColumnName(sourceTable)!);
                });
            }
            string create = $"""
                CREATE TRIGGER {trigger} ON {sourceName} AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    IF ROWCOUNT_BIG() = 0 RETURN;
                    SET NOCOUNT ON;
                    WITH changes AS (
                        SELECT r.{fk} AS targetID, CAST(1 AS bigint) AS delta FROM inserted AS r WHERE {filter}
                        UNION ALL
                        SELECT r.{fk}, CAST(-1 AS bigint) FROM deleted AS r WHERE {filter}
                    ), totals AS (
                        SELECT targetID, SUM(delta) AS delta FROM changes GROUP BY targetID HAVING SUM(delta) <> 0
                    )
                    UPDATE t SET {count} = t.{count} + d.delta
                    FROM {targetName} AS t JOIN totals AS d ON d.targetID = t.{id};
                END
                """;
            string backfill = $"""
                UPDATE t SET {count} = COALESCE(c.total, 0)
                FROM {targetName} AS t LEFT JOIN (
                    SELECT r.{fk} AS targetID, COUNT_BIG(*) AS total FROM {sourceName} AS r WHERE {filter} GROUP BY r.{fk}
                ) AS c ON c.targetID = t.{id}
                WHERE t.{count} <> COALESCE(c.total, 0);
                """;
            return new Definition(sourceTable.Schema!, sourceTable.Name, rule.Name, create.ReplaceLineEndings("\n"), backfill, COUNT_OWNER, null);
        }
    }
}
