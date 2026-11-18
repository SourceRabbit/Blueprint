using System.Collections;
using Blueprint.Database.Sql.RAMCache;
using Blueprint.Entities;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Blueprint.Database.Queries.QueryExecutors.Database;

namespace Blueprint.Database.Queries.QueryExecutors.RAM
{
    /// <summary>Executes eligible queries in RAM while preserving optimizations and protecting cached objects.</summary>
    internal sealed class QueryExecutor_RAM : QueryExecutor
    {
        private readonly QueryExecutor_Database fDatabaseExecutor;

        /// <summary>Connects the executor to the entity's cache and original provider.</summary>
        /// <param name="contract">The original query's entity class.</param>
        /// <param name="provider">The provider that owns the query.</param>
        /// <param name="language">The language ID used by filters and results.</param>
        /// <param name="databaseExecutor">The SQL executor used for transactions and unsupported cache queries.</param>
        internal QueryExecutor_RAM(Type contract, IQueryProvider provider, long language, QueryExecutor_Database databaseExecutor) : base(contract, provider, language)
        {
            fDatabaseExecutor = databaseExecutor;
        }

        /// <summary>Tries count and single-entity optimizations before general execution against the cache.</summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="context">The context reserved for this operation.</param>
        /// <param name="expression">The complete terminal expression.</param>
        /// <param name="result">The cached result when the query is supported.</param>
        /// <returns>True after successful RAM execution or SQL fallback, including null, zero, and empty results.</returns>
        /// <exception cref="Exception">Cache synchronization or query execution fails.</exception>
        internal override bool TryExecute<TResult>(BlueprintDatabaseContext context, Expression expression, out TResult result)
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
            IQueryable? cached = GetQuery(context, expression, includeRelated);
            if (cached == null)
            {
                return fDatabaseExecutor.TryExecute(context, expression, out result);
            }
            result = cached.Provider.Execute<TResult>(new EntityRoot(fProvider, cached).Visit(expression)!);
            return true;
        }

        /// <summary>Tries row selection and projection optimizations before general execution on independent cached copies.</summary>
        /// <typeparam name="TElement">The type of each result.</typeparam>
        /// <param name="context">The context reserved for this operation.</param>
        /// <param name="expression">The complete sequence expression.</param>
        /// <param name="items">The independent results when the cache supports the query.</param>
        /// <returns>True after successful RAM execution or SQL fallback, including null, zero, and empty results.</returns>
        /// <exception cref="Exception">Cache synchronization or query materialization fails.</exception>
        internal override bool TryExecuteList<TElement>(BlueprintDatabaseContext context, Expression expression, out List<TElement>? items)
        {
            items = null;
            bool includeRelated = typeof(BlueprintEntity).IsAssignableFrom(typeof(TElement));
            RAMCachedTable? table = GetTable(context, expression, includeRelated);
            if (table == null)
            {
                return fDatabaseExecutor.TryExecuteList(context, expression, out items);
            }
            if (typeof(TElement) == fContract ? TryExecutePage(table, context, expression, out items) : TryExecuteProjection(table, context, expression, out items))
            {
                return true;
            }
            IQueryable? cached = GetQuery(context, expression, includeRelated);
            if (cached == null)
            {
                return fDatabaseExecutor.TryExecuteList(context, expression, out items);
            }
            items = cached.Provider.CreateQuery<TElement>(new EntityRoot(fProvider, cached).Visit(expression)!).ToList();
            return true;
        }

        /// <summary>Reads related rows from synchronized RAM, using SQL inside transactions or without a cache.</summary>
        /// <param name="context">The active model context.</param>
        /// <param name="key">The mapped relationship key.</param>
        /// <param name="ids">The parent key values to match.</param>
        /// <returns>Independent scalar rows from the selected source.</returns>
        /// <exception cref="Exception">Cache synchronization or SQL execution fails.</exception>
        internal override List<BlueprintEntity> ReadRelated(BlueprintDatabaseContext context, IProperty key, long[] ids)
        {
            RAMCachedTable? table = BlueprintDatabase.ACTIVE_INSTANCE.GetCachedTable(fContract, context);
            if (table == null)
            {
                return fDatabaseExecutor.ReadRelated(context, key, ids);
            }
            BlueprintDatabase.ACTIVE_INSTANCE.SynchronizeCache(context);
            return table.GetRelatedRows(key, ids).Select(row => table.Copy(row, fLanguage)).ToList();
        }

        /// <summary>Selects a cached table only when the complete query can execute safely in RAM.</summary>
        /// <param name="context">The context reserved for the current query.</param>
        /// <param name="expression">The complete query expression.</param>
        /// <param name="includeRelated">Whether entity results require automatic relationships.</param>
        /// <returns>The synchronized table, or null when SQL is required.</returns>
        /// <exception cref="Exception">Required cache synchronization fails.</exception>
        private RAMCachedTable? GetTable(BlueprintDatabaseContext context, Expression expression, bool includeRelated)
        {
            RAMCachedTable? table = BlueprintDatabase.ACTIVE_INSTANCE.GetCachedTable(fContract, context);
            if (table == null || !CachedQueryEligibility.CanExecute(fContract, expression, fProvider, includeRelated))
            {
                return null;
            }
            BlueprintDatabase.ACTIVE_INSTANCE.SynchronizeCache(context);
            return table;
        }

        /// <summary>Creates a safe LINQ source from cached roots and relationships loaded through their own executors.</summary>
        /// <param name="context">The current context using the reserved context.</param>
        /// <param name="expression">The complete query expression.</param>
        /// <param name="includeRelated">Whether entity results require automatic relationships.</param>
        /// <returns>A query over independent copies, or null when SQL is required.</returns>
        /// <exception cref="Exception">Required cache synchronization or copying fails.</exception>
        private IQueryable? GetQuery(BlueprintDatabaseContext context, Expression expression, bool includeRelated)
        {
            RAMCachedTable? table = GetTable(context, expression, includeRelated);
            return table == null ? null : CreateQuery(table, context);
        }

        /// <summary>Returns the count or evaluates safe filters on existing objects without copying or changing the language.</summary>
        /// <param name="expression">The terminal Count or LongCount expression.</param>
        /// <param name="context">The context reserved for this operation.</param>
        /// <param name="count">The result as an int or long when counting completes.</param>
        /// <returns>True if safe counting without copies is supported.</returns>
        private bool TryExecuteCount(BlueprintDatabaseContext context, Expression expression, out object? count)
        {
            count = null;
            if (expression is not MethodCallExpression call || call.Method.DeclaringType != typeof(Queryable) || (call.Method.Name != nameof(Queryable.Count) && call.Method.Name != nameof(Queryable.LongCount)))
            {
                return false;
            }
            RAMCachedTable? table = GetTable(context, expression, false);
            if (table == null)
            {
                return false;
            }
            if (!TryCreateCountPredicate(call, table, context.EntityTypes.DefaultLanguageID, out Func<BlueprintEntity, bool>? predicate))
            {
                return false;
            }
            int matches = predicate == null ? table.Rows.Count : table.Rows.Values.Count(predicate);
            count = call.Method.Name == nameof(Queryable.LongCount) ? (object)(long)matches : matches;
            return true;
        }

        /// <summary>Combines consecutive Where filters and the optional Count predicate for safe reads of shared cached objects.</summary>
        /// <param name="expression">The terminal Count or LongCount expression.</param>
        /// <param name="table">The synchronized table snapshot and its stored properties.</param>
        /// <param name="defaultLanguage">The language used by fallback-enabled properties.</param>
        /// <param name="predicate">The safe filter, or null if there are no filters.</param>
        /// <returns>True if counting can run directly on the shared objects while preserving filter order and short-circuit behavior.</returns>
        /// <exception cref="Exception">Expression rewriting or compilation fails.</exception>
        private bool TryCreateCountPredicate(MethodCallExpression expression, RAMCachedTable table, long defaultLanguage, out Func<BlueprintEntity, bool>? predicate)
        {
            predicate = null;
            if (expression.Method.GetGenericArguments().Length != 1 || expression.Method.GetGenericArguments()[0] != fContract)
            {
                return false;
            }
            Stack<Expression> filters = new Stack<Expression>();
            if (expression.Arguments.Count == 2)
            {
                filters.Push(expression.Arguments[1]);
            }
            Expression source = expression.Arguments[0];
            while (source is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable) && call.Method.Name == nameof(Queryable.Where))
            {
                filters.Push(call.Arguments[1]);
                source = call.Arguments[0];
            }
            if (source is not ConstantExpression constant || constant.Value is not IQueryable query || !ReferenceEquals(query.Provider, fProvider) || query.ElementType != fContract)
            {
                return false;
            }
            if (filters.Count == 0)
            {
                return true;
            }
            CachedReadExpression visitor = new CachedReadExpression(fContract, table.ConcreteType, table.Properties, fLanguage, defaultLanguage);
            Expression? body = null;
            ParameterExpression? parameter = null;
            foreach (Expression filter in filters)
            {
                if (!visitor.TryRewriteLambda(filter, out LambdaExpression? lambda))
                {
                    return false;
                }
                parameter = lambda!.Parameters[0];
                body = body == null ? lambda.Body : Expression.AndAlso(body, lambda.Body);
            }
            predicate = Expression.Lambda<Func<BlueprintEntity, bool>>(body!, parameter!).Compile();
            return true;
        }

        /// <summary>Evaluates safe First and Single operations on shared rows and copies only the selected entity.</summary>
        /// <param name="expression">A supported terminal expression with an optional predicate.</param>
        /// <param name="context">The context reserved for this operation.</param>
        /// <param name="element">The independent result, or null for an empty OrDefault operation.</param>
        /// <returns>True if selection can execute without exposing shared entities.</returns>
        /// <exception cref="InvalidOperationException">First or Single has no match, or Single has multiple matches.</exception>
        private bool TryExecuteElement(BlueprintDatabaseContext context, Expression expression, out object? element)
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
            RAMCachedTable? table = GetTable(context, expression, true);
            if (table == null)
            {
                return false;
            }
            if (!TryCreatePage(call.Arguments[0], table, context.EntityTypes.DefaultLanguageID, out Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>? selection))
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
            if (element is BlueprintEntity entity)
            {
                EntityIncludes.Load(context, new[] { entity }, fLanguage);
            }
            return true;
        }

        /// <summary>Selects filtered lists and pages from shared objects and copies only the final results before returning.</summary>
        /// <typeparam name="TElement">The entity class.</typeparam>
        /// <param name="table">The synchronized table snapshot.</param>
        /// <param name="expression">The complete entity sequence, with or without paging.</param>
        /// <param name="context">The context reserved for this operation.</param>
        /// <param name="items">The independent result copies when the query executes.</param>
        /// <returns>True if the results may be selected from shared objects.</returns>
        private bool TryExecutePage<TElement>(RAMCachedTable table, BlueprintDatabaseContext context, Expression expression, out List<TElement>? items)
        {
            items = null;
            if (!TryCreatePage(expression, table, context.EntityTypes.DefaultLanguageID, out Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>? page))
            {
                return false;
            }
            List<TElement> results = new List<TElement>();
            foreach (BlueprintEntity stored in page!(table.Rows.Values))
            {
                results.Add((TElement)(object)table.Copy(stored, fLanguage));
            }
            items = results;
            EntityIncludes.Load(context, results.OfType<BlueprintEntity>(), fLanguage);
            return true;
        }

        /// <summary>Translates supported operations to Enumerable, preserving the exact order of filters, sorting, and limits.</summary>
        /// <param name="expression">The complete entity sequence, with or without paging.</param>
        /// <param name="table">The synchronized table snapshot and its stored properties.</param>
        /// <param name="defaultLanguage">The language used by fallback-enabled properties.</param>
        /// <param name="page">The safe selection of shared objects to copy.</param>
        /// <returns>True if results can be selected safely before making independent copies.</returns>
        /// <exception cref="Exception">Expression rewriting or compilation fails.</exception>
        private bool TryCreatePage(Expression expression, RAMCachedTable table, long defaultLanguage, out Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>? page)
        {
            page = null;
            Stack<MethodCallExpression> operations = new Stack<MethodCallExpression>();
            while (expression is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable))
            {
                if (!call.Method.IsGenericMethod || call.Method.GetGenericArguments()[0] != fContract || call.Arguments.Count < 2 || call.Arguments.Count > 3)
                {
                    return false;
                }
                operations.Push(call);
                expression = call.Arguments[0];
            }
            if (expression is not ConstantExpression constant || constant.Value is not IQueryable query || !ReferenceEquals(query.Provider, fProvider) || query.ElementType != fContract)
            {
                return false;
            }
            CachedReadExpression visitor = new CachedReadExpression(fContract, table.ConcreteType, table.Properties, fLanguage, defaultLanguage);
            ParameterExpression rows = Expression.Parameter(typeof(IEnumerable<BlueprintEntity>), "rows");
            Expression source = rows;
            foreach (MethodCallExpression operation in operations)
            {
                string name = operation.Method.Name;
                switch (name)
                {
                    case nameof(Queryable.Where):
                        if (operation.Arguments.Count != 2 || !visitor.TryRewriteLambda(operation.Arguments[1], out LambdaExpression? predicate))
                        {
                            return false;
                        }
                        source = Expression.Call(typeof(Enumerable), name, new[] { typeof(BlueprintEntity) }, source, predicate!);
                        break;
                    case nameof(Queryable.OrderBy):
                    case nameof(Queryable.OrderByDescending):
                    case nameof(Queryable.ThenBy):
                    case nameof(Queryable.ThenByDescending):
                        if (!visitor.TryRewriteLambda(operation.Arguments[1], out LambdaExpression? key))
                        {
                            return false;
                        }
                        if ((name == nameof(Queryable.ThenBy) || name == nameof(Queryable.ThenByDescending)) && !typeof(IOrderedEnumerable<BlueprintEntity>).IsAssignableFrom(source.Type))
                        {
                            return false;
                        }
                        List<Expression> arguments = new List<Expression> { source, key! };
                        if (operation.Arguments.Count == 3)
                        {
                            if (operation.Arguments[2] is not ConstantExpression)
                            {
                                return false;
                            }
                            arguments.Add(operation.Arguments[2]);
                        }
                        source = Expression.Call(typeof(Enumerable), name, new[] { typeof(BlueprintEntity), key!.ReturnType }, arguments.ToArray());
                        break;
                    case nameof(Queryable.Skip):
                    case nameof(Queryable.Take):
                        if (operation.Arguments.Count != 2 || operation.Arguments[1] is not ConstantExpression { Value: int })
                        {
                            return false;
                        }
                        source = Expression.Call(typeof(Enumerable), name, new[] { typeof(BlueprintEntity) }, source, operation.Arguments[1]);
                        break;
                    default:
                        return false;
                }
            }
            page = Expression.Lambda<Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>>(source, rows).Compile();
            return true;
        }

        /// <summary>Reads only the selected fields and builds independent results without copying complete cached entities.</summary>
        /// <typeparam name="TElement">The projected result type.</typeparam>
        /// <param name="table">The synchronized table snapshot.</param>
        /// <param name="expression">The complete projected sequence.</param>
        /// <param name="context">The context reserved for this operation.</param>
        /// <param name="items">The independent projected results when execution succeeds.</param>
        /// <returns>True if the projection can execute without exposing mutable cached values.</returns>
        private bool TryExecuteProjection<TElement>(RAMCachedTable table, BlueprintDatabaseContext context, Expression expression, out List<TElement>? items)
        {
            items = null;
            Func<Expression, Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>?> createSelection = source => TryCreatePage(source, table, context.EntityTypes.DefaultLanguageID, out Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>? page) ? page : null;
            if (!CachedProjection.TryCreate<TElement>(expression, createSelection, fContract, table.ConcreteType, table.Properties, fLanguage, context.EntityTypes.DefaultLanguageID, out Func<IEnumerable<BlueprintEntity>, IEnumerable<TElement>>? projection))
            {
                return false;
            }
            items = projection!(table.Rows.Values).ToList();
            return true;
        }

        /// <summary>Copies cached data so that changes to properties, arrays, and language remain private to the caller.</summary>
        /// <param name="table">The synchronized table snapshot.</param>
        /// <param name="context">The current model used for relationship reads.</param>
        /// <returns>A LINQ query over independent instances of the public entity class.</returns>
        private IQueryable CreateQuery(RAMCachedTable table, BlueprintDatabaseContext context)
        {
            IReadOnlyDictionary<long, BlueprintEntity> rows = table.Rows;
            Array entities = Array.CreateInstance(fContract, rows.Count);
            int index = 0;
            foreach (BlueprintEntity stored in rows.Values)
            {
                entities.SetValue(table.Copy(stored, fLanguage), index++);
            }
            EntityIncludes.Load(context, entities.Cast<BlueprintEntity>(), fLanguage);
            return ((IEnumerable)entities).AsQueryable();
        }

    }
}
