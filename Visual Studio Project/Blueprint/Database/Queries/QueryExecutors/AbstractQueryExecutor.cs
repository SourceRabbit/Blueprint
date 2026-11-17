using Blueprint.Database.Contexts;
using System.Linq.Expressions;

namespace Blueprint.Database.Queries.QueryExecutors
{
    /// <summary>Defines execution of complete queries under the database lock and allows fallback to the next executor.</summary>
    internal abstract class AbstractQueryExecutor
    {
        protected readonly IQueryProvider fProvider;
        protected readonly long fLanguage;

        /// <summary>Stores the original provider and the language used by filters and results.</summary>
        /// <param name="provider">The provider that owns the original expression.</param>
        /// <param name="language">The selected language ID.</param>
        protected AbstractQueryExecutor(IQueryProvider provider, long language)
        {
            fProvider = provider;
            fLanguage = language;
        }

        /// <summary>Executes a terminal operation or indicates that the next executor is required.</summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="expression">The complete expression to execute.</param>
        /// <param name="result">The result when execution is supported, including null or zero.</param>
        /// <returns>True if the query executed; otherwise false. Execution failures propagate as exceptions.</returns>
        /// <exception cref="Exception">Query translation, synchronization, or execution fails.</exception>
        internal abstract bool TryExecute<TResult>(EntityContext context, Expression expression, out TResult result);

        /// <summary>Materializes the complete result list or indicates that the next executor is required.</summary>
        /// <typeparam name="TElement">The type of each result.</typeparam>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="expression">The complete sequence expression.</param>
        /// <param name="items">The results when execution is supported, including an empty list.</param>
        /// <returns>True if the query executed; otherwise false. Execution failures propagate as exceptions.</returns>
        /// <exception cref="Exception">Query translation, synchronization, or materialization fails.</exception>
        internal abstract bool TryExecuteList<TElement>(EntityContext context, Expression expression, out List<TElement>? items);

        /// <summary>Replaces the original query root with the selected executor's data source.</summary>
        protected sealed class EntityRoot : ExpressionVisitor
        {
            private readonly IQueryProvider fProvider;
            private readonly IQueryable fRoot;

            /// <summary>Connects the original provider to the source that will execute the expression.</summary>
            /// <param name="provider">The original entity query's provider.</param>
            /// <param name="root">The RAM or EF Core data source.</param>
            internal EntityRoot(IQueryProvider provider, IQueryable root)
            {
                fProvider = provider;
                fRoot = root;
            }

            /// <summary>Replaces only query roots that belong to the specified provider.</summary>
            /// <param name="node">The constant expression to examine.</param>
            /// <returns>The new root expression or the original constant.</returns>
            protected override Expression VisitConstant(ConstantExpression node)
            {
                return node.Value is IQueryable query && ReferenceEquals(query.Provider, fProvider) ? fRoot.Expression : base.VisitConstant(node);
            }
        }
    }
}
