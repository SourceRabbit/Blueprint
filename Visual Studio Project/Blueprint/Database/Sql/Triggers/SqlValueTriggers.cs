// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Blueprint.Database.Sql.Triggers
{
    /// <summary>Generates set-based updates for the row containing a formula and tables supplying its inputs.</summary>
    internal static class SqlValueTriggers
    {
        private sealed record Reference(INavigation Navigation, string Alias, string Table, string Key, string ForeignKey);

        /// <summary>Resolves the formula through the EF model and emits one trigger for each input table reference.</summary>
        internal static IEnumerable<SqlEntityTriggers.Definition> Build(BlueprintDatabaseContext context, ISqlGenerationHelper sql, ValueTrigger rule)
        {
            IEntityType entity = context.Model.FindEntityType(context.EntityTypes.Types[rule.Entity])!;
            StoreObjectIdentifier table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string owner = sql.DelimitIdentifier(table.Name, table.Schema);
            IProperty target = entity.FindProperty(rule.Target)!;
            string targetColumn = sql.DelimitIdentifier(target.GetColumnName(table)!);
            string key = sql.DelimitIdentifier(entity.FindPrimaryKey()!.Properties.Single().GetColumnName(table)!);
            Dictionary<string, Reference> references = new Dictionary<string, Reference>(StringComparer.Ordinal);
            foreach (ValueInput input in rule.Inputs.Where(input => input.Navigation != null))
            {
                string name = input.Navigation!.Name;
                if (references.ContainsKey(name))
                {
                    continue;
                }
                INavigation? navigation = entity.FindNavigation(name);
                if (navigation == null || !navigation.IsOnDependent || navigation.ForeignKey.Properties.Count != 1 || navigation.ForeignKey.PrincipalKey.Properties.Count != 1)
                {
                    throw new InvalidOperationException($"MaintainValue '{rule.Name}' requires a mapped single-column reference '{name}'.");
                }
                IEntityType principal = navigation.TargetEntityType;
                StoreObjectIdentifier related = StoreObjectIdentifier.Table(principal.GetTableName()!, principal.GetSchema());
                references.Add(name, new Reference(navigation, "p" + references.Count, sql.DelimitIdentifier(related.Name, related.Schema),
                    sql.DelimitIdentifier(navigation.ForeignKey.PrincipalKey.Properties.Single().GetColumnName(related)!),
                    sql.DelimitIdentifier(navigation.ForeignKey.Properties.Single().GetColumnName(table)!)));
            }

            Dictionary<ValueInput, string> columns = new Dictionary<ValueInput, string>();
            foreach (ValueInput input in rule.Inputs)
            {
                IEntityType source = input.Navigation == null ? entity : references[input.Navigation.Name].Navigation.TargetEntityType;
                IProperty property = source.FindProperty(input.Property.Name) ?? throw new InvalidOperationException($"MaintainValue '{rule.Name}' requires a stored decimal '{input.Property.Name}'.");
                columns.Add(input, sql.DelimitIdentifier(property.GetColumnName(StoreObjectIdentifier.Table(source.GetTableName()!, source.GetSchema()))!));
            }
            string formula = rule.Translate(input => (input.Navigation == null ? "r" : references[input.Navigation.Name].Alias) + "." + columns[input]);
            string joins = string.Join("\n", references.Values.Select(reference =>
                $"LEFT JOIN {reference.Table} AS {reference.Alias} WITH (HOLDLOCK) ON {reference.Alias}.{reference.Key} = r.{reference.ForeignKey}"));
            string Update(string scope) => $"""
                UPDATE r SET {targetColumn} = calculated.amount
                FROM {owner} AS r
                {scope}
                {joins}
                CROSS APPLY (VALUES (CONVERT({target.GetColumnType()}, {formula}))) AS calculated(amount)
                WHERE EXISTS (SELECT r.{targetColumn} EXCEPT SELECT calculated.amount);
                """;
            string backfill = Update(string.Empty);
            IEnumerable<string> watched = columns.Where(pair => pair.Key.Navigation == null).Select(pair => pair.Value)
                .Concat(references.Values.Select(reference => reference.ForeignKey)).Append(targetColumn);
            yield return Create(sql, rule.Name, entity, owner, watched, Update($"JOIN inserted AS i ON i.{key} = r.{key}"), backfill);

            foreach ((string name, Reference reference) in references)
            {
                IEnumerable<string> inputs = columns.Where(pair => pair.Key.Navigation?.Name == name).Select(pair => pair.Value);
                string dependencyName = rule.Name + "_" + name;
                if (dependencyName.Length > 128)
                {
                    throw new InvalidOperationException($"MaintainValue dependency trigger '{dependencyName}' exceeds 128 characters.");
                }
                yield return Create(sql, dependencyName, reference.Navigation.TargetEntityType, owner, inputs,
                    Update($"JOIN inserted AS i ON i.{reference.Key} = r.{reference.ForeignKey}"), backfill);
            }
        }

        /// <summary>Restricts updates to affected rows and columns and avoids firing another update for an equal value.</summary>
        private static SqlEntityTriggers.Definition Create(ISqlGenerationHelper sql, string name, IEntityType entity, string owner, IEnumerable<string> inputs, string update, string backfill)
        {
            string table = entity.GetTableName()!;
            string schema = entity.GetSchema()!;
            string guard = string.Join(" OR ", inputs.Distinct(StringComparer.Ordinal).Select(column => $"UPDATE({column})"));
            string create = $"""
                CREATE TRIGGER {sql.DelimitIdentifier(name, schema)} ON {sql.DelimitIdentifier(table, schema)} AFTER INSERT, UPDATE
                AS
                BEGIN
                    IF ROWCOUNT_BIG() = 0 RETURN;
                    SET NOCOUNT ON;
                    IF NOT ({guard}) RETURN;
                    {update}
                END
                """;
            return new SqlEntityTriggers.Definition(schema, table, name, create.ReplaceLineEndings("\n"), backfill, SqlEntityTriggers.VALUE_OWNER, owner);
        }
    }
}
