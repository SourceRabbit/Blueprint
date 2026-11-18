using Blueprint.Entities;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace Blueprint.Database.Queries.QueryExecutors.RAM
{
    /// <summary>Builds projected results from safe cached field reads without copying complete entities.</summary>
    internal static class CachedProjection
    {
        /// <summary>Combines entity selection, a safe Select, and supported result operations in their original order.</summary>
        /// <typeparam name="TElement">The projected result type.</typeparam>
        /// <param name="expression">The complete projected sequence.</param>
        /// <param name="createSelection">Creates a safe selection for the entity source before Select, or returns null when unsupported.</param>
        /// <param name="contract">The entity class.</param>
        /// <param name="concreteType">The generated type of the cached entities.</param>
        /// <param name="properties">The stored properties of the entity.</param>
        /// <param name="language">The selected language.</param>
        /// <param name="defaultLanguage">The language used by fallback-enabled properties.</param>
        /// <param name="projection">The sequence factory that returns only independent projected values.</param>
        /// <returns>True if the projection can execute without exposing shared entities or their mutable fields.</returns>
        internal static bool TryCreate<TElement>(Expression expression, Func<Expression, Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>?> createSelection, Type contract, Type concreteType, IProperty[] properties, long language, long defaultLanguage, out Func<IEnumerable<BlueprintEntity>, IEnumerable<TElement>>? projection)
        {
            projection = null;
            Stack<MethodCallExpression> operations = new Stack<MethodCallExpression>();
            while (expression is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable) && call.Method.Name != nameof(Queryable.Select))
            {
                if (!call.Method.IsGenericMethod || call.Method.GetGenericArguments()[0] != typeof(TElement) || call.Arguments.Count < 1 || call.Arguments.Count > 3)
                {
                    return false;
                }
                operations.Push(call);
                expression = call.Arguments[0];
            }
            if (expression is not MethodCallExpression select || select.Method.DeclaringType != typeof(Queryable) || select.Method.Name != nameof(Queryable.Select) || !select.Method.IsGenericMethod || select.Arguments.Count != 2)
            {
                return false;
            }
            Type[] types = select.Method.GetGenericArguments();
            if (types.Length != 2 || types[0] != contract || types[1] != typeof(TElement))
            {
                return false;
            }
            CachedReadExpression visitor = new CachedReadExpression(contract, concreteType, properties, language, defaultLanguage);
            if (!visitor.TryRewriteLambda(select.Arguments[1], out LambdaExpression? selector, allowProjection: true))
            {
                return false;
            }
            Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>>? selection = createSelection(select.Arguments[0]);
            if (selection == null)
            {
                return false;
            }
            ParameterExpression rows = Expression.Parameter(typeof(IEnumerable<BlueprintEntity>), "rows");
            Expression source = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select), new[] { typeof(BlueprintEntity), typeof(TElement) }, Expression.Invoke(Expression.Constant(selection), rows), selector!);
            foreach (MethodCallExpression operation in operations)
            {
                string name = operation.Method.Name;
                switch (name)
                {
                    case nameof(Queryable.Where):
                    case nameof(Queryable.OrderBy):
                    case nameof(Queryable.OrderByDescending):
                    case nameof(Queryable.ThenBy):
                    case nameof(Queryable.ThenByDescending):
                        if (operation.Arguments.Count < 2 || operation.Arguments[1] is not UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression lambda } || lambda.Parameters.Count != 1 || lambda.Parameters[0].Type != typeof(TElement))
                        {
                            return false;
                        }
                        if ((name == nameof(Queryable.ThenBy) || name == nameof(Queryable.ThenByDescending)) && !typeof(IOrderedEnumerable<TElement>).IsAssignableFrom(source.Type))
                        {
                            return false;
                        }
                        List<Expression> arguments = new List<Expression> { source, lambda };
                        if (operation.Arguments.Count == 3)
                        {
                            if (operation.Arguments[2] is not ConstantExpression)
                            {
                                return false;
                            }
                            arguments.Add(operation.Arguments[2]);
                        }
                        source = Expression.Call(typeof(Enumerable), name, operation.Method.GetGenericArguments(), arguments.ToArray());
                        break;
                    case nameof(Queryable.Skip):
                    case nameof(Queryable.Take):
                        if (operation.Arguments.Count != 2 || operation.Arguments[1] is not ConstantExpression { Value: int })
                        {
                            return false;
                        }
                        source = Expression.Call(typeof(Enumerable), name, new[] { typeof(TElement) }, source, operation.Arguments[1]);
                        break;
                    case nameof(Queryable.Distinct):
                        if (operation.Arguments.Count == 1)
                        {
                            source = Expression.Call(typeof(Enumerable), name, new[] { typeof(TElement) }, source);
                        }
                        else if (operation.Arguments.Count == 2 && operation.Arguments[1] is ConstantExpression)
                        {
                            source = Expression.Call(typeof(Enumerable), name, new[] { typeof(TElement) }, source, operation.Arguments[1]);
                        }
                        else
                        {
                            return false;
                        }
                        break;
                    default:
                        return false;
                }
            }
            projection = Expression.Lambda<Func<IEnumerable<BlueprintEntity>, IEnumerable<TElement>>>(source, rows).Compile();
            return true;
        }
    }
}
