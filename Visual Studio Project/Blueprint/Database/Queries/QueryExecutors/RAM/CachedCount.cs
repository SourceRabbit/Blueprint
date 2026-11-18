using Blueprint.Database.Entities;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace Blueprint.Database.Queries.QueryExecutors.RAM
{
    /// <summary>Prepares Count with filters that read the shared cache without mutations or escaping mutable references.</summary>
    internal static class CachedCount
    {
        /// <summary>Combines consecutive Where filters and the optional Count predicate, preserving order and short-circuit behavior.</summary>
        /// <param name="expression">The terminal Count or LongCount expression.</param>
        /// <param name="provider">The provider of the single root.</param>
        /// <param name="contract">The entity class.</param>
        /// <param name="concreteType">The generated class of the cached objects.</param>
        /// <param name="properties">The model's stored properties.</param>
        /// <param name="language">The language of the filters.</param>
        /// <param name="defaultLanguage">The language used by fallback-enabled properties.</param>
        /// <param name="predicate">The safe filter, or null if there are no filters.</param>
        /// <returns>True if counting can run directly on the shared objects.</returns>
        internal static bool TryCreatePredicate(MethodCallExpression expression, IQueryProvider provider, Type contract, Type concreteType, IProperty[] properties, long language, long defaultLanguage, out Func<BlueprintEntity, bool>? predicate)
        {
            predicate = null;
            if (expression.Method.GetGenericArguments().Length != 1 || expression.Method.GetGenericArguments()[0] != contract)
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
            if (source is not ConstantExpression constant || constant.Value is not IQueryable query || !ReferenceEquals(query.Provider, provider) || query.ElementType != contract)
            {
                return false;
            }
            if (filters.Count == 0)
            {
                return true;
            }
            CachedReadExpression visitor = new CachedReadExpression(contract, concreteType, properties, language, defaultLanguage);
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
    }
}
