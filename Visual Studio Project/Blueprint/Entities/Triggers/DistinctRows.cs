// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;

namespace Blueprint.Entities.Triggers
{
    /// <summary>Provides SQL query roots for a distinct-row declaration during schema synchronization.</summary>
    public sealed class DistinctRowsQuery
    {
        internal DistinctRowsQuery(BlueprintDatabaseContext context)
        {
            Context = context;
        }

        internal BlueprintDatabaseContext Context { get; }

        /// <summary>References the mapped table for a registered entity in a rule query.</summary>
        /// <typeparam name="T">The registered entity contract.</typeparam>
        /// <returns>A relational query root for the rule projection.</returns>
        public IQueryable<T> Rows<T>() where T : BlueprintEntity
        {
            return Context.Query<T>();
        }

        internal IQueryable<T> Changes<T>() where T : BlueprintEntity
        {
            MethodInfo method = typeof(DistinctRowsQuery).GetMethod(nameof(ChangeSet), BindingFlags.Instance | BindingFlags.NonPublic)!;
            IQueryable rows = (IQueryable)method.MakeGenericMethod(Context.EntityTypes.Types[typeof(T)]).Invoke(this, null)!;
            return rows.Cast<T>();
        }

        private IQueryable<T> ChangeSet<T>() where T : class
        {
            return Context.Set<T>().FromSqlRaw("SELECT * FROM inserted UNION ALL SELECT * FROM deleted");
        }
    }

    internal sealed record DistinctRowsChange(string Name, Type Entity, string[] Watch, Func<DistinctRowsQuery, IQueryable> Keys, bool ReplaceExisting);
    internal sealed record DistinctRowsValues(Type Entity, string ForeignKey, string Value, string Projection);

    internal sealed class DistinctRowsRule(string name, Type target, string[] key, Func<DistinctRowsQuery, IQueryable> rows)
    {
        internal string Name { get; } = name;
        internal Type Target { get; } = target;
        internal string[] Key { get; } = key;
        internal Func<DistinctRowsQuery, IQueryable> Rows { get; } = rows;
        internal List<DistinctRowsChange> Changes { get; } = new List<DistinctRowsChange>();
        internal DistinctRowsValues? Values { get; set; }

        internal static string[] Properties(LambdaExpression selector)
        {
            Expression expression = selector.Body;
            if (expression is UnaryExpression { NodeType: ExpressionType.Convert } cast)
            {
                expression = cast.Operand;
            }
            if (expression is MemberExpression { Member: PropertyInfo single } member && member.Expression == selector.Parameters[0])
            {
                return [single.Name];
            }
            if (expression is NewExpression { Members: not null } tuple && tuple.Arguments.Count > 0)
            {
                return tuple.Arguments.Select((argument, index) =>
                    argument is MemberExpression { Member: PropertyInfo property } access && access.Expression == selector.Parameters[0]
                        && property.Name == tuple.Members[index].Name ? property.Name
                        : throw new ArgumentException("Select direct properties using their original names.", nameof(selector))).ToArray();
            }
            throw new ArgumentException("Select one property or an anonymous key of direct properties.", nameof(selector));
        }
    }

    /// <summary>Declares the changed keys and optional distinct child values for a maintained table.</summary>
    /// <typeparam name="TKey">Named key properties shared by the owner and changed-key projections.</typeparam>
    /// <typeparam name="TRow">The desired row projection.</typeparam>
    public sealed class DistinctRowsBuilder<TKey, TRow>
    {
        private readonly DistinctRowsRule fRule;

        internal DistinctRowsBuilder(DistinctRowsRule rule)
        {
            fRule = rule;
        }

        /// <summary>Identifies affected keys from the old and new images of a statement on a source table.</summary>
        /// <typeparam name="TSource">The entity receiving the source trigger.</typeparam>
        /// <param name="name">SQL trigger name on the source table.</param>
        /// <param name="keys">A composable projection using the same named key columns as the target.</param>
        /// <param name="watch">Optional properties to compare to skip irrelevant updates. Inserts and deletes always participate.</param>
        /// <param name="replaceExisting">Explicitly adopt a legacy unowned trigger with this name on this source table.</param>
        /// <returns>The current rule for additional declarations.</returns>
        public DistinctRowsBuilder<TKey, TRow> OnChange<TSource>(string name, Func<DistinctRowsQuery, IQueryable<TSource>, IQueryable<TKey>> keys,
            Expression<Func<TSource, object>>? watch = null, bool replaceExisting = false) where TSource : BlueprintEntity
        {
            EntityTriggerBuilder<TSource>.ValidateName(name);
            ArgumentNullException.ThrowIfNull(keys);
            fRule.Changes.Add(new DistinctRowsChange(name, typeof(TSource), watch == null ? [] : DistinctRowsRule.Properties(watch),
                query => keys(query, query.Changes<TSource>()), replaceExisting));
            return this;
        }

        /// <summary>Stores nonempty, distinct string values under the stable ID of each maintained row.</summary>
        /// <typeparam name="TChild">The child table owned by this rule.</typeparam>
        /// <param name="foreignKey">The child's required foreign key to the maintained parent.</param>
        /// <param name="value">The child's required string column.</param>
        /// <param name="sourceValue">The string property of the desired row projection.</param>
        /// <returns>The current rule for additional declarations.</returns>
        public DistinctRowsBuilder<TKey, TRow> WithValues<TChild>(Expression<Func<TChild, long>> foreignKey, Expression<Func<TChild, string>> value,
            Expression<Func<TRow, string?>> sourceValue) where TChild : BlueprintEntity
        {
            if (fRule.Values != null)
            {
                throw new InvalidOperationException("A distinct-row rule already owns a values table.");
            }
            ArgumentNullException.ThrowIfNull(foreignKey);
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(sourceValue);
            fRule.Values = new DistinctRowsValues(typeof(TChild), DistinctRowsRule.Properties(foreignKey).Single(),
                DistinctRowsRule.Properties(value).Single(), DistinctRowsRule.Properties(sourceValue).Single());
            return this;
        }
    }
}
