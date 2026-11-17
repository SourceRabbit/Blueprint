using System.Collections;
using Blueprint.Database.Sql.RAMCache;
using Blueprint.Database.Contexts;
using Blueprint.Database.Entities;
using System.Linq.Expressions;

namespace Blueprint.Database.Queries.QueryExecutors.RAMCache
{
    /// <summary>Executes eligible queries in RAM while preserving optimizations and protecting cached objects.</summary>
    internal sealed class RAMCacheQueryExecutor : AbstractQueryExecutor
    {
        private readonly BlueprintDatabase fDatabase;
        private readonly Type fContract;

        /// <summary>Connects the executor to the entity's cache and original provider.</summary>
        /// <param name="database">The database that manages the cache.</param>
        /// <param name="contract">The original query's entity interface.</param>
        /// <param name="provider">The provider that owns the query.</param>
        /// <param name="language">The language ID used by filters and results.</param>
        internal RAMCacheQueryExecutor(BlueprintDatabase database, Type contract, IQueryProvider provider, long language) : base(provider, language)
        {
            fDatabase = database;
            fContract = contract;
        }

        /// <summary>Tries count and single-entity optimizations before general execution against the cache.</summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="expression">The complete terminal expression.</param>
        /// <param name="result">The cached result when the query is supported.</param>
        /// <returns>True if the query executed in RAM; otherwise false to allow SQL execution.</returns>
        /// <exception cref="Exception">Cache synchronization or query execution fails.</exception>
        internal override bool TryExecute<TResult>(EntityContext context, Expression expression, out TResult result)
        {
            if (TryExecuteCount(context, expression, out object? count))
            {
                result = (TResult)count!;
                return true;
            }
            if (TryExecuteElement(context, expression, out object? element))
            {
                result = (TResult)element!;
                return true;
            }
            bool includeRelated = typeof(BlueprintEntity).IsAssignableFrom(typeof(TResult));
            IQueryable? cached = GetQuery(expression, includeRelated);
            if (cached == null)
            {
                result = default!;
                return false;
            }
            result = cached.Provider.Execute<TResult>(new EntityRoot(fProvider, cached).Visit(expression)!);
            return true;
        }

        /// <summary>Tries row selection and projection optimizations before general execution on independent cached copies.</summary>
        /// <typeparam name="TElement">The type of each result.</typeparam>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="expression">The complete sequence expression.</param>
        /// <param name="items">The independent results when the cache supports the query.</param>
        /// <returns>True if the query executed in RAM; otherwise false to allow SQL execution.</returns>
        /// <exception cref="Exception">Cache synchronization or query materialization fails.</exception>
        internal override bool TryExecuteList<TElement>(EntityContext context, Expression expression, out List<TElement>? items)
        {
            items = null;
            bool includeRelated = typeof(BlueprintEntity).IsAssignableFrom(typeof(TElement));
            RAMCachedTable? table = GetTable(expression, includeRelated);
            if (table == null)
            {
                return false;
            }
            if (typeof(TElement) == fContract ? TryExecutePage(table, context, expression, out items) : TryExecuteProjection(table, context, expression, out items))
            {
                return true;
            }
            IQueryable? cached = GetQuery(expression, includeRelated);
            if (cached == null)
            {
                return false;
            }
            items = cached.Provider.CreateQuery<TElement>(new EntityRoot(fProvider, cached).Visit(expression)!).ToList();
            return true;
        }

        /// <summary>Selects a cached table only when the complete query can execute safely in RAM.</summary>
        /// <param name="expression">The complete query expression.</param>
        /// <param name="includeRelated">Whether entity results require automatic relationships.</param>
        /// <returns>The synchronized table, or null when SQL is required.</returns>
        /// <exception cref="Exception">Required cache synchronization fails.</exception>
        private RAMCachedTable? GetTable(Expression expression, bool includeRelated)
        {
            RAMCachedTable? table = fDatabase.GetCachedTable(fContract);
            if (table == null || !CachedQueryEligibility.CanExecute(fContract, expression, fProvider, includeRelated))
            {
                return null;
            }
            fDatabase.SynchronizeCache();
            return table;
        }

        /// <summary>Creates a safe LINQ source from the current cached snapshot.</summary>
        /// <param name="expression">The complete query expression.</param>
        /// <param name="includeRelated">Whether entity results require automatic relationships.</param>
        /// <returns>A query over independent copies, or null when SQL is required.</returns>
        /// <exception cref="Exception">Required cache synchronization or copying fails.</exception>
        private IQueryable? GetQuery(Expression expression, bool includeRelated)
        {
            RAMCachedTable? table = GetTable(expression, includeRelated);
            return table == null ? null : CreateQuery(table);
        }

        /// <summary>Returns the count or evaluates safe filters on existing objects without copying or changing the language.</summary>
        /// <param name="expression">The terminal Count or LongCount expression.</param>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="count">The result as an int or long when counting completes.</param>
        /// <returns>True if safe counting without copies is supported.</returns>
        private bool TryExecuteCount(EntityContext context, Expression expression, out object? count)
        {
            count = null;
            if (expression is not MethodCallExpression call || call.Method.DeclaringType != typeof(Queryable) || (call.Method.Name != nameof(Queryable.Count) && call.Method.Name != nameof(Queryable.LongCount)))
            {
                return false;
            }
            RAMCachedTable? table = GetTable(expression, false);
            if (table == null)
            {
                return false;
            }
            if (!CachedCount.TryCreatePredicate(call, fProvider, fContract, table.ConcreteType, table.Properties, fLanguage, context.EntityTypes.DefaultLanguageID, out Func<BlueprintEntity, bool>? predicate))
            {
                return false;
            }
            int matches = predicate == null ? table.Rows.Count : table.Rows.Values.Count(predicate);
            count = call.Method.Name == nameof(Queryable.LongCount) ? (object)(long)matches : matches;
            return true;
        }

        /// <summary>Evaluates safe First and Single operations on shared rows and copies only the selected entity.</summary>
        /// <param name="expression">A supported terminal expression with an optional predicate.</param>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="element">The independent result, or null for an empty OrDefault operation.</param>
        /// <returns>True if selection can execute without exposing shared entities.</returns>
        /// <exception cref="InvalidOperationException">First or Single has no match, or Single has multiple matches.</exception>
        private bool TryExecuteElement(EntityContext context, Expression expression, out object? element)
        {
            element = null;
            if (expression is not MethodCallExpression call || call.Method.DeclaringType != typeof(Queryable) || call.Type != fContract || call.Arguments.Count is < 1 or > 2)
            {
                return false;
            }
            if (call.Method.Name is not (nameof(Queryable.First) or nameof(Queryable.FirstOrDefault) or nameof(Queryable.Single) or nameof(Queryable.SingleOrDefault)))
            {
                return false;
            }
            RAMCachedTable? table = GetTable(expression, true);
            if (table == null)
            {
                return false;
            }
            if (!CachedPage.TryCreate(call.Arguments[0], fProvider, fContract, table.ConcreteType, table.Properties, fLanguage, context.EntityTypes.DefaultLanguageID, out Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>? selection))
            {
                return false;
            }
            Func<BlueprintEntity, bool>? predicate = null;
            if (call.Arguments.Count == 2)
            {
                CachedReadExpression visitor = new CachedReadExpression(fContract, table.ConcreteType, table.Properties, fLanguage, context.EntityTypes.DefaultLanguageID);
                if (!visitor.TryRewriteLambda(call.Arguments[1], out LambdaExpression? lambda))
                {
                    return false;
                }
                predicate = (Func<BlueprintEntity, bool>)lambda!.Compile();
            }
            IEnumerable<BlueprintEntity> rows = selection!(table.Rows.Values);
            BlueprintEntity? stored = call.Method.Name switch
            {
                nameof(Queryable.First) => predicate == null ? rows.First() : rows.First(predicate),
                nameof(Queryable.FirstOrDefault) => predicate == null ? rows.FirstOrDefault() : rows.FirstOrDefault(predicate),
                nameof(Queryable.Single) => predicate == null ? rows.Single() : rows.Single(predicate),
                nameof(Queryable.SingleOrDefault) => predicate == null ? rows.SingleOrDefault() : rows.SingleOrDefault(predicate),
                _ => throw new InvalidOperationException("Unsupported cached element operation.")
            };
            element = stored == null ? null : table.Copy(stored, fLanguage);
            return true;
        }

        /// <summary>Selects filtered lists and pages from shared objects and copies only the final results before returning.</summary>
        /// <typeparam name="TElement">The table interface.</typeparam>
        /// <param name="table">The synchronized table snapshot.</param>
        /// <param name="expression">The complete entity sequence, with or without paging.</param>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="items">The independent result copies when the query executes.</param>
        /// <returns>True if the results may be selected from shared objects.</returns>
        private bool TryExecutePage<TElement>(RAMCachedTable table, EntityContext context, Expression expression, out List<TElement>? items)
        {
            items = null;
            if (!CachedPage.TryCreate(expression, fProvider, fContract, table.ConcreteType, table.Properties, fLanguage, context.EntityTypes.DefaultLanguageID, out Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>? page))
            {
                return false;
            }
            List<TElement> results = new List<TElement>();
            foreach (BlueprintEntity stored in page!(table.Rows.Values))
            {
                results.Add((TElement)(object)table.Copy(stored, fLanguage));
            }
            items = results;
            return true;
        }

        /// <summary>Reads only the selected fields and builds independent results without copying complete cached entities.</summary>
        /// <typeparam name="TElement">The projected result type.</typeparam>
        /// <param name="table">The synchronized table snapshot.</param>
        /// <param name="expression">The complete projected sequence.</param>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="items">The independent projected results when execution succeeds.</param>
        /// <returns>True if the projection can execute without exposing mutable cached values.</returns>
        private bool TryExecuteProjection<TElement>(RAMCachedTable table, EntityContext context, Expression expression, out List<TElement>? items)
        {
            items = null;
            if (!CachedProjection.TryCreate<TElement>(expression, fProvider, fContract, table.ConcreteType, table.Properties, fLanguage, context.EntityTypes.DefaultLanguageID, out Func<IEnumerable<BlueprintEntity>, IEnumerable<TElement>>? projection))
            {
                return false;
            }
            items = projection!(table.Rows.Values).ToList();
            return true;
        }

        /// <summary>Copies cached data so that changes to properties, arrays, and language remain private to the caller.</summary>
        /// <param name="table">The synchronized table snapshot.</param>
        /// <returns>A LINQ query over independent instances of the public interface.</returns>
        private IQueryable CreateQuery(RAMCachedTable table)
        {
            Array entities = Array.CreateInstance(fContract, table.Rows.Count);
            int index = 0;
            foreach (BlueprintEntity stored in table.Rows.Values)
            {
                entities.SetValue(table.Copy(stored, fLanguage), index++);
            }
            return ((IEnumerable)entities).AsQueryable();
        }

    }
}
