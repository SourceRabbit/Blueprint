// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database.Sql.RAMCache;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Blueprint.Database.Queries.QueryExecutors.RAM
{
    /// <summary>Shares compiled selections and scalar projections for the lifetime of a cached table while binding each execution's own arguments.</summary>
    internal sealed class CachedPagePlans
    {
        private const int MAX_PLANS_PER_TABLE = 256;
        private static readonly ConditionalWeakTable<RAMCachedTable, CachedPagePlans> fTables = new();
        private static readonly ParameterExpression fArguments = Expression.Parameter(typeof(object[]), "arguments");
        private readonly Dictionary<Expression, Delegate> fPlans = new(ExpressionEqualityComparer.Instance);
        private readonly Queue<Expression> fInsertionOrder = new();

        /// <summary>Binds a validated query plan to its caller's constants after rewriting multilingual property access.</summary>
        /// <typeparam name="TInput">The row or sequence accepted by the plan.</typeparam>
        /// <typeparam name="TResult">The projected value or selected sequence.</typeparam>
        /// <param name="table">The table whose model supplied the mapped properties.</param>
        /// <param name="input">The source parameter of the plan.</param>
        /// <param name="source">The translated plan body.</param>
        /// <returns>A delegate bound to the current execution's arguments.</returns>
        internal static Func<TInput, TResult> Bind<TInput, TResult>(RAMCachedTable table, ParameterExpression input, Expression source)
        {
            PageArguments arguments = new PageArguments();
            Expression body = arguments.Visit(source);
            var template = Expression.Lambda<Func<TInput, object?[], TResult>>(body, input, fArguments);
            CachedPagePlans cache = fTables.GetValue(table, static _ => new CachedPagePlans());
            var plan = cache.GetOrCompile(template);
            object?[] values = arguments.fValues.ToArray();
            return current => plan(current, values);
        }

        /// <summary>Compiles each resident shape once and evicts the oldest shape when the table's cache is full.</summary>
        /// <typeparam name="TDelegate">The signature included in the expression cache key.</typeparam>
        /// <param name="template">The plan with typed argument slots in place of caller constants.</param>
        /// <returns>The shared executable selection.</returns>
        private TDelegate GetOrCompile<TDelegate>(Expression<TDelegate> template) where TDelegate : Delegate
        {
            lock (fPlans)
            {
                if (fPlans.TryGetValue(template, out Delegate? cached))
                {
                    return (TDelegate)cached;
                }
                TDelegate plan = template.Compile();
                if (fPlans.Count == MAX_PLANS_PER_TABLE)
                {
                    fPlans.Remove(fInsertionOrder.Dequeue());
                }
                fPlans.Add(template, plan);
                fInsertionOrder.Enqueue(template);
                return plan;
            }
        }

        /// <summary>Separates literal and captured inputs from the expression used as a structural cache key.</summary>
        private sealed class PageArguments : ExpressionVisitor
        {
            internal readonly List<object?> fValues = new();

            /// <summary>Replaces the constant with its typed argument slot, leaving closure member reads deferred.</summary>
            /// <param name="node">The original input, including nulls, comparers and captured objects.</param>
            /// <returns>A typed read from the current execution's argument array.</returns>
            protected override Expression VisitConstant(ConstantExpression node)
            {
                int index = fValues.Count;
                fValues.Add(node.Value);
                return Expression.Convert(Expression.ArrayIndex(fArguments, Expression.Constant(index)), node.Type);
            }
        }
    }
}
