// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace Blueprint.Database.Sql.Triggers
{
    /// <summary>Compiles typed source projections with the active SQL provider and maintains their distinct keys.</summary>
    internal static class SqlDistinctRows
    {
        internal const string OWNER = "Blueprint.MaintainDistinctRows";
        internal sealed record Procedure(string Schema, string Name, string Target, string Create, string Backfill);
        internal sealed record Plan(Procedure Procedure, List<SqlEntityTriggers.Definition> Triggers);
        private sealed record KeyColumn(string Alias, string Physical);

        internal static Plan Build(BlueprintDatabaseContext context, ISqlGenerationHelper sql, DistinctRowsRule rule)
        {
            DistinctRowsQuery query = new DistinctRowsQuery(context);
            IEntityType Map(Type type) => context.Model.FindEntityType(context.EntityTypes.Types[type])!;
            IEntityType target = Map(rule.Target);
            string table = sql.DelimitIdentifier(target.GetTableName()!, target.GetSchema());
            string procedure = sql.DelimitIdentifier(rule.Name, target.GetSchema());
            string Column(IEntityType entity, string name)
            {
                IProperty property = entity.FindProperty(name) ?? throw new InvalidOperationException($"Distinct-row rule '{rule.Name}' requires a stored property '{entity.Name}.{name}'.");
                return sql.DelimitIdentifier(property.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
            }
            RequireKey(target, rule.Key);
            KeyColumn[] key = rule.Key.Select(name => new KeyColumn(sql.DelimitIdentifier(name), Column(target, name))).ToArray();
            string Keys(string alias) => string.Join(", ", key.Select(k => alias + "." + k.Alias));
            string Match(string left, string right, bool physical = false) => string.Join(" AND ", key.Select(k => left + "." + (physical ? k.Physical : k.Alias) + " = " + right + "." + k.Alias));
            string nonNull = string.Join(" AND ", key.Select(k => "r." + k.Alias + " IS NOT NULL"));
            string physicalKeys = string.Join(", ", key.Select(k => k.Physical));
            string keyAliases = string.Join(", ", key.Select(k => k.Alias));
            string source = QuerySql(rule.Rows(query));
            string rows = $"SELECT DISTINCT {Keys("r")} INTO #bp_rows FROM ({source}) r JOIN #bp_keys k ON {Match("r", "k")};";
            string children = string.Empty;
            if (rule.Values is DistinctRowsValues values)
            {
                IEntityType child = Map(values.Entity);
                RequireKey(child, [values.ForeignKey, values.Value]);
                if (!child.GetForeignKeys().Any(fk => fk.PrincipalEntityType == target && fk.Properties.Count == 1 && fk.Properties[0].Name == values.ForeignKey))
                {
                    throw new InvalidOperationException($"'{values.Entity.Name}.{values.ForeignKey}' must reference '{rule.Target.Name}'.");
                }
                string childTable = sql.DelimitIdentifier(child.GetTableName()!, child.GetSchema());
                string fk = Column(child, values.ForeignKey);
                string value = Column(child, values.Value);
                string projected = sql.DelimitIdentifier(values.Projection);
                rows = $"""
                    SELECT {Keys("r")}, NULLIF(MIN(COALESCE(v.Val, N'') COLLATE Latin1_General_100_BIN2), N'') COLLATE DATABASE_DEFAULT AS [__bp_value]
                    INTO #bp_rows FROM ({source}) r JOIN #bp_keys k ON {Match("r", "k")}
                    CROSS APPLY (VALUES (NULLIF(r.{projected} COLLATE DATABASE_DEFAULT, N''))) v(Val)
                    GROUP BY {Keys("r")}, v.Val;
                    """;
                children = $"""
                    SELECT t.ID, {Keys("k")} INTO #bp_parents FROM {table} t JOIN #bp_keys k ON {Match("t", "k", true)};
                    DELETE v FROM {childTable} v JOIN #bp_parents p ON p.ID = v.{fk}
                    WHERE NOT EXISTS (SELECT 1 FROM #bp_rows r WHERE {Match("r", "p")} AND r.[__bp_value] = v.{value});
                    UPDATE v SET {value} = r.[__bp_value] FROM {childTable} v JOIN #bp_parents p ON p.ID = v.{fk}
                    JOIN #bp_rows r ON {Match("r", "p")} AND r.[__bp_value] = v.{value}
                    WHERE r.[__bp_value] COLLATE Latin1_General_100_BIN2 <> v.{value} COLLATE Latin1_General_100_BIN2;
                    INSERT {childTable} ({fk}, {value})
                    SELECT p.ID, r.[__bp_value] FROM #bp_rows r JOIN #bp_parents p ON {Match("r", "p")}
                    WHERE r.[__bp_value] IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM {childTable} v WHERE v.{fk} = p.ID AND v.{value} = r.[__bp_value]);
                    """;
            }
            string create = $"""
                CREATE PROCEDURE {procedure} AS
                BEGIN
                    SET NOCOUNT ON;
                    DECLARE @locked bigint;
                    SELECT @locked = t.ID FROM #bp_keys k LEFT JOIN {table} t WITH (UPDLOCK, HOLDLOCK, FORCESEEK)
                        ON {Match("t", "k", true)} ORDER BY {Keys("k")};
                    {rows}
                    CREATE CLUSTERED INDEX IX_bp_rows ON #bp_rows ({keyAliases});
                    INSERT {table} ({physicalKeys}) SELECT DISTINCT {Keys("r")} FROM #bp_rows r
                    WHERE NOT EXISTS (SELECT 1 FROM {table} t WHERE {Match("t", "r", true)});
                    {children}
                    DELETE t FROM {table} t JOIN #bp_keys k ON {Match("t", "k", true)}
                    WHERE NOT EXISTS (SELECT 1 FROM #bp_rows r WHERE {Match("r", "k")});
                END
                """;
            string backfill = $"""
                SELECT {Keys("r")} INTO #bp_keys FROM ({source}) r WHERE {nonNull}
                UNION SELECT {physicalKeys} FROM {table};
                EXEC {procedure};
                DROP TABLE #bp_keys;
                """;
            Procedure routine = new Procedure(target.GetSchema()!, rule.Name, table, create.ReplaceLineEndings("\n"), backfill);
            List<SqlEntityTriggers.Definition> triggers = new List<SqlEntityTriggers.Definition>();
            foreach (DistinctRowsChange change in rule.Changes)
            {
                IEntityType entity = Map(change.Entity);
                string sourceTable = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                string name = sql.DelimitIdentifier(change.Name, entity.GetSchema());
                string guard = string.Empty;
                if (change.Watch.Length > 0)
                {
                    string watch = string.Join(", ", change.Watch.Prepend("ID").Distinct().Select(property =>
                        entity.FindProperty(property)?.ClrType == typeof(string) ? $"CONVERT(varbinary(max), {Column(entity, property)})" : Column(entity, property)));
                    guard = $"IF NOT EXISTS (SELECT {watch} FROM inserted EXCEPT SELECT {watch} FROM deleted) AND NOT EXISTS (SELECT {watch} FROM deleted EXCEPT SELECT {watch} FROM inserted) RETURN;";
                }
                string keys = QuerySql(change.Keys(query));
                string trigger = $"""
                    CREATE TRIGGER {name} ON {sourceTable} AFTER INSERT, UPDATE, DELETE AS
                    BEGIN
                        IF ROWCOUNT_BIG() = 0 RETURN;
                        SET NOCOUNT ON;
                        {guard}
                        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
                        SELECT DISTINCT {Keys("r")} INTO #bp_keys FROM ({keys}) r WHERE {nonNull};
                        IF EXISTS (SELECT 1 FROM #bp_keys) EXEC {procedure};
                    END
                    """;
                triggers.Add(new SqlEntityTriggers.Definition(entity.GetSchema()!, entity.GetTableName()!, change.Name,
                    trigger.ReplaceLineEndings("\n"), backfill, OWNER, table, change.ReplaceExisting));
            }
            return new Plan(routine, triggers);
        }

        private static string QuerySql(IQueryable projection)
        {
            using (DbCommand command = projection.CreateDbCommand())
            {
                if (command.Parameters.Count != 0)
                {
                    throw new NotSupportedException("Distinct-row projections must be composable, parameter-free SQL queries. Use stable literal criteria in the declaration.");
                }
                return command.CommandText;
            }
        }

        private static void RequireKey(IEntityType entity, string[] properties)
        {
            if (!entity.GetIndexes().Any(index => index.IsUnique && index.GetFilter() == null && index.Properties.Select(p => p.Name).SequenceEqual(properties))
                || properties.Any(name => entity.FindProperty(name)!.IsNullable))
            {
                throw new InvalidOperationException($"'{entity.Name}' requires a non-null unique index on ({string.Join(", ", properties)}).");
            }
        }
    }
}
