using Blueprint.Database.Entities;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Blueprint.Database.Queries.QueryExecutors.Database
{
    /// <summary>Translates and executes queries through EF Core while preserving the language and visitor order.</summary>
    internal sealed class QueryExecutor_Database : QueryExecutor
    {
        /// <summary>Binds SQL execution to the root contract and its database.</summary>
        /// <param name="database">The database owning the query.</param>
        /// <param name="contract">The entity class to read.</param>
        /// <param name="provider">The provider that owns the original expression.</param>
        /// <param name="language">The language ID used by filters and results.</param>
        internal QueryExecutor_Database(BlueprintDatabase database, Type contract, IQueryProvider provider, long language) : base(database, contract, provider, language)
        {
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
            IQueryable root = context.Query(fContract);
            long previousLanguage = context.QueryLanguage;
            try
            {
                context.QueryLanguage = fLanguage;
                result = root.Provider.Execute<TResult>(Translate(expression, root, context.EntityTypes.DefaultLanguageID));
                if (result is BlueprintEntity entity)
                {
                    EntityIncludes.Load(fDatabase, context, new[] { entity }, fLanguage);
                }
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
            IQueryable root = context.Query(fContract);
            long previousLanguage = context.QueryLanguage;
            try
            {
                context.QueryLanguage = fLanguage;
                items = root.Provider.CreateQuery<TElement>(Translate(expression, root, context.EntityTypes.DefaultLanguageID)).ToList();
                EntityIncludes.Load(fDatabase, context, items.OfType<BlueprintEntity>(), fLanguage);
                return true;
            }
            finally
            {
                context.QueryLanguage = previousLanguage;
            }
        }

        /// <summary>Fetches related scalar rows in parameterized batches without SQL Include joins.</summary>
        /// <param name="context">The active model context.</param>
        /// <param name="key">The mapped relationship key.</param>
        /// <param name="ids">The parent key values to match.</param>
        /// <returns>Independent rows with the requested language selected.</returns>
        /// <exception cref="Exception">Query construction or SQL execution fails.</exception>
        internal override List<BlueprintEntity> ReadRelated(BlueprintDatabaseContext context, IProperty key, long[] ids)
        {
            IQueryable root = context.Query(fContract);
            ParameterExpression parameter = Expression.Parameter(root.ElementType, "entity");
            Expression value = Expression.Property(parameter, key.PropertyInfo!);
            Expression predicate = value.Type == typeof(long?)
                ? Expression.AndAlso(Expression.Property(value, "HasValue"), Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(long) }, Expression.Constant(ids), Expression.Property(value, "Value")))
                : Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(long) }, Expression.Constant(ids), value);
            Expression filter = Expression.Call(typeof(Queryable), nameof(Queryable.Where), new[] { root.ElementType }, root.Expression, Expression.Quote(Expression.Lambda(predicate, parameter)));
            long previousLanguage = context.QueryLanguage;
            try
            {
                context.QueryLanguage = fLanguage;
                return root.Provider.CreateQuery(filter).Cast<BlueprintEntity>().ToList();
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
