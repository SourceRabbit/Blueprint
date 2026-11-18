// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Linq.Expressions;

namespace Blueprint.Database.Queries
{
    /// <summary>Stores an entity query or projection without reading data until execution.</summary>
    /// <typeparam name="TElement">The result element type.</typeparam>
    internal sealed class EntityQuery<TElement> : IOrderedQueryable<TElement>
    {
        private readonly EntityQueryProvider fProvider;
        private readonly Expression fExpression;

        /// <summary>Creates an expression root bound to the selected database and language.</summary>
        /// <param name="provider">The provider that executes the query.</param>
        internal EntityQuery(EntityQueryProvider provider)
        {
            fProvider = provider;
            fExpression = System.Linq.Expressions.Expression.Constant(this);
        }

        /// <summary>Preserves a composed expression for deferred execution by its original provider.</summary>
        /// <param name="provider">The provider that owns the expression root.</param>
        /// <param name="expression">The composed query expression.</param>
        internal EntityQuery(EntityQueryProvider provider, Expression expression)
        {
            fProvider = provider;
            fExpression = expression;
        }

        /// <summary>Materializes the results using a reserved context before exposing their enumerator.</summary>
        /// <returns>The result enumerator.</returns>
        /// <exception cref="Exception">Query translation, synchronization, or execution fails.</exception>
        public IEnumerator<TElement> GetEnumerator()
        {
            return fProvider.ExecuteList<TElement>(fExpression).GetEnumerator();
        }

        /// <summary>Supports enumeration through the non-generic interface.</summary>
        /// <returns>The result enumerator.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public Type ElementType
        {
            get
            {
                return typeof(TElement);
            }
        }

        public Expression Expression
        {
            get
            {
                return fExpression;
            }
        }

        public IQueryProvider Provider
        {
            get
            {
                return fProvider;
            }
        }
    }
}
