using Blueprint.Database.Entities;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace Blueprint.Database.Queries.QueryExecutors.RAMCache
{
    /// <summary>Selects shared cached objects using safe filters, ordering, and optional paging before copying them.</summary>
    internal static class CachedPage
    {
        /// <summary>Translates supported operations to Enumerable, preserving the exact order of filters, sorting, and limits.</summary>
        /// <param name="expression">The complete entity sequence, with or without paging.</param>
        /// <param name="provider">The provider of the single root.</param>
        /// <param name="contract">The result interface.</param>
        /// <param name="concreteType">The generated class of the cached objects.</param>
        /// <param name="properties">The model's stored properties.</param>
        /// <param name="language">The language of the filters and sorting keys.</param>
        /// <param name="defaultLanguage">The language used by fallback-enabled properties.</param>
        /// <param name="page">The safe selection of shared objects to copy.</param>
        /// <returns>True if results can be selected safely before making independent copies.</returns>
        internal static bool TryCreate(Expression expression, IQueryProvider provider, Type contract, Type concreteType, IProperty[] properties, long language, long defaultLanguage, out Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>? page)
        {
            page = null;
            Stack<MethodCallExpression> operations = new Stack<MethodCallExpression>();
            while (expression is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable))
            {
                if (!call.Method.IsGenericMethod || call.Method.GetGenericArguments()[0] != contract || call.Arguments.Count < 2 || call.Arguments.Count > 3)
                {
                    return false;
                }
                operations.Push(call);
                expression = call.Arguments[0];
            }
            if (expression is not ConstantExpression constant || constant.Value is not IQueryable query || !ReferenceEquals(query.Provider, provider) || query.ElementType != contract)
            {
                return false;
            }
            CachedReadExpression visitor = new CachedReadExpression(contract, concreteType, properties, language, defaultLanguage);
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
    }
}
