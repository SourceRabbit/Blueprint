// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Linq.Expressions;
using System.Reflection;

namespace Blueprint.Entities.Triggers
{
    /// <summary>Declares database triggers on changes to rows of a source entity.</summary>
    /// <typeparam name="TEntity">The entity on which the trigger runs.</typeparam>
    public sealed class EntityTriggerBuilder<TEntity> where TEntity : BlueprintEntity
    {
        private readonly EntityTriggerDeclarations fTriggers;

        /// <summary>Collects rules while Blueprint registers the entity model.</summary>
        /// <param name="triggers">The rules of the current model.</param>
        internal EntityTriggerBuilder(EntityTriggerDeclarations triggers)
        {
            fTriggers = triggers;
        }

        /// <summary>Maintains a target entity's integer counter on inserts, updates and deletes of matching source rows.</summary>
        /// <typeparam name="TTarget">The entity referenced by the foreign key.</typeparam>
        /// <param name="name">The SQL trigger name, unique within its schema.</param>
        /// <param name="foreignKey">A mapped long or nullable long foreign key referencing the target ID.</param>
        /// <param name="counter">A stored integer property on the target.</param>
        /// <param name="include">Optional predicate over non-nullable Boolean, enum and integer fields and literal constants.</param>
        /// <exception cref="ArgumentException">The name or a property selector is invalid.</exception>
        public void MaintainCount<TTarget>(string name, Expression<Func<TEntity, long?>> foreignKey, Expression<Func<TTarget, int>> counter, Expression<Func<TEntity, bool>>? include = null) where TTarget : BlueprintEntity
        {
            ValidateName(name);
            fTriggers.Counts.Add(new CountTrigger(name, typeof(TEntity), typeof(TTarget), GetProperty(foreignKey), GetProperty(counter), include));
        }

        /// <summary>Maintains a stored decimal from decimal arithmetic on the row and its referenced rows.</summary>
        /// <typeparam name="TValue">A decimal or nullable decimal.</typeparam>
        /// <param name="name">The name of the trigger on the maintained entity.</param>
        /// <param name="target">The stored property that receives the result.</param>
        /// <param name="value">The formula using mapped properties, decimal literals and arithmetic.</param>
        public void MaintainValue<TValue>(string name, Expression<Func<TEntity, TValue>> target, Expression<Func<TEntity, TValue>> value)
        {
            ValidateName(name);
            if (typeof(TValue) != typeof(decimal) && typeof(TValue) != typeof(decimal?))
            {
                throw new ArgumentException("MaintainValue requires a decimal or nullable decimal target.", nameof(target));
            }
            ArgumentNullException.ThrowIfNull(value);
            fTriggers.Values.Add(new ValueTrigger(name, typeof(TEntity), GetProperty(target), value));
        }

        /// <summary>Validates a SQL Server trigger identifier before registration.</summary>
        private static void ValidateName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (name.Length > 128 || name != name.Trim() || name.Any(char.IsControl))
            {
                throw new ArgumentException("A trigger name must be a SQL identifier of at most 128 characters.", nameof(name));
            }
        }

        /// <summary>Resolves a direct property selector, including a required key lifted to nullable long.</summary>
        /// <param name="selector">The source or target selector.</param>
        /// <returns>The mapped property's CLR name.</returns>
        /// <exception cref="ArgumentException">The selector is not a direct property access.</exception>
        private static string GetProperty(LambdaExpression selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            Expression body = selector.Body;
            if (body is UnaryExpression { NodeType: ExpressionType.Convert, Type: Type type } conversion && Nullable.GetUnderlyingType(type) == conversion.Operand.Type)
            {
                body = conversion.Operand;
            }
            if (body is not MemberExpression { Member: PropertyInfo property } member || member.Expression != selector.Parameters[0])
            {
                throw new ArgumentException("Select a property directly on the entity.", nameof(selector));
            }
            return property.Name;
        }
    }
}
