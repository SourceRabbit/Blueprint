using System.Linq.Expressions;
using System.Reflection;
using Blueprint.Database.Queries.QueryExecutors;
using Blueprint.Database.Queries.QueryExecutors.RAMCache;
using Blueprint.Database.Queries.QueryExecutors.Sql;
using Blueprint.Database.Contexts;

namespace Blueprint.Database.Queries
{
    /// <summary>Preserves LINQ composition and delegates execution to RAM cache or SQL executors under the database lock.</summary>
    internal sealed class EntityQueryProvider : IQueryProvider
    {
        private readonly BlueprintDatabase fDatabase;
        private readonly long fLanguage;
        private readonly AbstractQueryExecutor[] fExecutors;

        /// <summary>Connects the provider to the database and orders RAM cache execution before SQL fallback.</summary>
        /// <param name="database">The query's database.</param>
        /// <param name="contract">The root entity interface.</param>
        /// <param name="queryFactory">The function that creates the root with optional automatic relationships.</param>
        /// <param name="language">The language of the filters and results.</param>
        internal EntityQueryProvider(BlueprintDatabase database, Type contract, Func<EntityContext, bool, IQueryable> queryFactory, long language)
        {
            fDatabase = database;
            fLanguage = language;
            fExecutors = new AbstractQueryExecutor[]
            {
                new RAMCacheQueryExecutor(database, contract, this, language),
                new SqlQueryExecutor(queryFactory, this, language)
            };
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

        /// <summary>Delegates a terminal LINQ operation to the first executor that supports it under the database lock.</summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="expression">The complete expression.</param>
        /// <returns>The result from the cache or database.</returns>
        /// <exception cref="Exception">Query execution fails, or no executor accepts the expression.</exception>
        public TResult Execute<TResult>(Expression expression)
        {
            return fDatabase.ExecuteOperation(context =>
            {
                _ = context.EntityTypes.getLanguage(fLanguage);
                foreach (AbstractQueryExecutor executor in fExecutors)
                {
                    if (executor.TryExecute(context, expression, out TResult result))
                    {
                        return result;
                    }
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

        /// <summary>Delegates list materialization to the first supported executor while holding the database lock.</summary>
        /// <typeparam name="TElement">The type of each result.</typeparam>
        /// <param name="expression">The complete query.</param>
        /// <returns>The results from the cache or database.</returns>
        /// <exception cref="Exception">Query execution fails, or no executor accepts the expression.</exception>
        internal List<TElement> ExecuteList<TElement>(Expression expression)
        {
            return fDatabase.ExecuteOperation(context =>
            {
                _ = context.EntityTypes.getLanguage(fLanguage);
                foreach (AbstractQueryExecutor executor in fExecutors)
                {
                    if (executor.TryExecuteList<TElement>(context, expression, out List<TElement>? items))
                    {
                        return items!;
                    }
                }
                throw new InvalidOperationException("No query executor accepted the expression.");
            });
        }

    }
}
