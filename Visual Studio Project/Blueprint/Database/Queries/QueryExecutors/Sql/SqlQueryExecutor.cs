using Blueprint.Database.Entities;
using System.Linq.Expressions;

namespace Blueprint.Database.Queries.QueryExecutors.Sql
{
    /// <summary>Translates and executes queries through EF Core while preserving the language and visitor order.</summary>
    internal sealed class SqlQueryExecutor : AbstractQueryExecutor
    {
        private readonly Func<BlueprintDatabaseContext, bool, IQueryable> fQueryFactory;

        /// <summary>Stores the factory that creates an EF root from each execution's active context.</summary>
        /// <param name="queryFactory">The factory that creates the root with optional relationship loading.</param>
        /// <param name="provider">The provider that owns the original expression.</param>
        /// <param name="language">The language ID used by filters and results.</param>
        internal SqlQueryExecutor(Func<BlueprintDatabaseContext, bool, IQueryable> queryFactory, IQueryProvider provider, long language) : base(provider, language)
        {
            fQueryFactory = queryFactory;
        }

        /// <summary>Executes the terminal expression through EF Core and restores the language even on failure.</summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="expression">The complete terminal expression.</param>
        /// <param name="result">The result returned by the EF provider.</param>
        /// <returns>True after successful execution. Failures propagate as exceptions.</returns>
        /// <exception cref="Exception">Root creation, query translation, or execution fails.</exception>
        internal override bool TryExecute<TResult>(BlueprintDatabaseContext context, Expression expression, out TResult result)
        {
            bool includeRelated = typeof(BlueprintEntity).IsAssignableFrom(typeof(TResult));
            IQueryable root = fQueryFactory(context, includeRelated);
            long previousLanguage = context.QueryLanguage;
            try
            {
                context.QueryLanguage = fLanguage;
                result = root.Provider.Execute<TResult>(Translate(expression, root, context.EntityTypes.DefaultLanguageID));
                return true;
            }
            finally
            {
                context.QueryLanguage = previousLanguage;
            }
        }

        /// <summary>Materializes results through EF Core before releasing the lock and restores the language.</summary>
        /// <typeparam name="TElement">The type of each result.</typeparam>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="expression">The complete sequence expression.</param>
        /// <param name="items">The result list returned by the EF provider.</param>
        /// <returns>True after successful materialization. Failures propagate as exceptions.</returns>
        /// <exception cref="Exception">Root creation, query translation, or materialization fails.</exception>
        internal override bool TryExecuteList<TElement>(BlueprintDatabaseContext context, Expression expression, out List<TElement>? items)
        {
            bool includeRelated = typeof(BlueprintEntity).IsAssignableFrom(typeof(TElement));
            IQueryable root = fQueryFactory(context, includeRelated);
            long previousLanguage = context.QueryLanguage;
            try
            {
                context.QueryLanguage = fLanguage;
                items = root.Provider.CreateQuery<TElement>(Translate(expression, root, context.EntityTypes.DefaultLanguageID)).ToList();
                return true;
            }
            finally
            {
                context.QueryLanguage = previousLanguage;
            }
        }

        /// <summary>Applies root replacement, multilingual mapping, and text search in the configured order.</summary>
        /// <param name="expression">The original query expression.</param>
        /// <param name="root">The EF Core root.</param>
        /// <param name="defaultLanguage">The default language ID used for fallback.</param>
        /// <returns>The expression to translate through the SQL provider.</returns>
        /// <exception cref="Exception">A visitor rejects the expression or a transformation fails.</exception>
        private Expression Translate(Expression expression, IQueryable root, long defaultLanguage)
        {
            QueryExpressionPipeline pipeline = new QueryExpressionPipeline();
            pipeline.AddVisitor(() => new EntityRoot(fProvider, root));
            pipeline.AddVisitor(() => new MultilingualQuery(fLanguage, defaultLanguage));
            pipeline.AddVisitor(() => new TextSearchQuery());
            return pipeline.Apply(expression);
        }
    }
}
