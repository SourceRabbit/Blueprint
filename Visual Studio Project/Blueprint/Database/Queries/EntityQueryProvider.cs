// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Linq.Expressions;
using System.Reflection;
using Blueprint.Database.Queries.QueryExecutors;

namespace Blueprint.Database.Queries
{
    /// <summary>Preserves LINQ composition and delegates execution to RAM cache or SQL executors using the reserved context.</summary>
    internal sealed class EntityQueryProvider : IQueryProvider
    {
        private readonly long fLanguage;
        private readonly QueryExecutor fExecutor;

        /// <summary>Connects the query to the executor selected by the root entity's base class.</summary>
        /// <param name="contract">The root entity class.</param>
        /// <param name="language">The language of the filters and results.</param>
        internal EntityQueryProvider(Type contract, long language)
        {
            fLanguage = language;
            fExecutor = QueryExecutor.Create(contract, this, language);
        }

        /// <summary>Builds a derived query without accessing the database.</summary>
        /// <typeparam name="TElement">The result type.</typeparam>
        /// <param name="expression">The new query's expression.</param>
        /// <returns>The derived query.</returns>
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new EntityQuery<TElement>(this, expression);
        }

        /// <summary>Builds a query when the result type is known only at runtime.</summary>
        /// <param name="expression">The query expression.</param>
        /// <returns>The derived query.</returns>
        public IQueryable CreateQuery(Expression expression)
        {
            Type queryType = expression.Type.GetInterfaces().Append(expression.Type).First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>));
            return (IQueryable)Activator.CreateInstance(typeof(EntityQuery<>).MakeGenericType(queryType.GetGenericArguments()[0]), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { this, expression }, null)!;
        }

        /// <summary>Delegates a terminal LINQ operation to the entity's executor using the reserved context.</summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="expression">The complete expression.</param>
        /// <returns>The result from the cache or database.</returns>
        /// <exception cref="Exception">Query execution fails, or no executor accepts the expression.</exception>
        public TResult Execute<TResult>(Expression expression)
        {
            return BlueprintDatabase.ACTIVE_INSTANCE.ExecuteOperation(context =>
            {
                _ = context.EntityTypes.getLanguage(fLanguage);
                if (fExecutor.TryExecute(context, expression, out TResult result))
                {
                    return result;
                }
                throw new InvalidOperationException("No query executor accepted the expression.");
            });
        }

        /// <summary>Executes the expression through the non-generic provider.</summary>
        /// <param name="expression">The expression to execute.</param>
        /// <returns>The execution result.</returns>
        public object? Execute(Expression expression)
        {
            return Execute<object?>(expression);
        }

        /// <summary>Delegates list materialization to the entity's executor using the reserved context.</summary>
        /// <typeparam name="TElement">The type of each result.</typeparam>
        /// <param name="expression">The complete query.</param>
        /// <returns>The results from the cache or database.</returns>
        /// <exception cref="Exception">Query execution fails, or no executor accepts the expression.</exception>
        internal List<TElement> ExecuteList<TElement>(Expression expression)
        {
            return BlueprintDatabase.ACTIVE_INSTANCE.ExecuteOperation(context =>
            {
                _ = context.EntityTypes.getLanguage(fLanguage);
                if (fExecutor.TryExecuteList<TElement>(context, expression, out List<TElement>? items))
                {
                    return items!;
                }
                throw new InvalidOperationException("No query executor accepted the expression.");
            });
        }

    }
}
