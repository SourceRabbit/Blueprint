using Blueprint.Database.Entities.Annotations;
using Blueprint.Database.Entities;
using Blueprint.Localization;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;
using System.Reflection;

namespace Blueprint.Database.Queries.QueryExecutors.RAM
{
    /// <summary>Checks filters, sorting keys, and projections for safe reads of the shared cache without mutable references.</summary>
    internal sealed class CachedReadExpression : ExpressionVisitor
    {
        private readonly Type fContract;
        private readonly Type fConcreteType;
        private readonly long fLanguage;
        private readonly long fDefaultLanguage;
        private readonly Dictionary<string, PropertyInfo> fContractProperties;
        private readonly Dictionary<string, IProperty> fStoredProperties;
        private readonly ParameterExpression fEntity = Expression.Parameter(typeof(BlueprintEntity), "entity");
        private ParameterExpression? fParameter;
        private bool fCanExecute = true;
        private bool fAllowProjection;

        /// <summary>Maps public properties to stored values in a specific language.</summary>
        /// <param name="contract">The public entity class.</param>
        /// <param name="concreteType">The generated class of the cached objects.</param>
        /// <param name="properties">The model's stored properties.</param>
        /// <param name="language">The language of the filters.</param>
        /// <param name="defaultLanguage">The language used when a selected translation is missing.</param>
        internal CachedReadExpression(Type contract, Type concreteType, IProperty[] properties, long language, long defaultLanguage)
        {
            fContract = contract;
            fConcreteType = concreteType;
            fLanguage = language;
            fDefaultLanguage = defaultLanguage;
            fContractProperties = BlueprintEntityTypes.GetProperties(contract).ToDictionary(property => property.Name);
            fStoredProperties = properties.ToDictionary(property => property.Name);
        }

        /// <summary>Validates a filter, sorting key, or projection and translates it into safe shared-cache reads.</summary>
        /// <param name="expression">The quoted lambda with one entity parameter.</param>
        /// <param name="lambda">The safe lambda with a shared BlueprintEntity-typed parameter.</param>
        /// <param name="allowProjection">Whether constructors and initializers may build independent result objects from safe values.</param>
        /// <returns>True if shared mutable data cannot be modified or escape.</returns>
        internal bool TryRewriteLambda(Expression expression, out LambdaExpression? lambda, bool allowProjection = false)
        {
            lambda = null;
            if (expression is not UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression input } || input.Parameters.Count != 1 || input.Parameters[0].Type != fContract)
            {
                return false;
            }
            fParameter = input.Parameters[0];
            fCanExecute = true;
            fAllowProjection = allowProjection;
            Expression body = Visit(input.Body)!;
            if (!fCanExecute)
            {
                return false;
            }
            lambda = Expression.Lambda(body, fEntity);
            return true;
        }

        /// <summary>Allows only reads and rejects assignments, ref/out calls, nested lambdas, and unchecked nodes.</summary>
        /// <param name="node">The filter node, or null.</param>
        /// <returns>The safe replacement, or the original node if execution requires copies.</returns>
        public override Expression? Visit(Expression? node)
        {
            if (node == null || !fCanExecute)
            {
                return node;
            }
            MethodInfo? method = node switch
            {
                MethodCallExpression call => call.Method,
                BinaryExpression binary => binary.Method,
                UnaryExpression unary => unary.Method,
                _ => null
            };
            if (method != null && (method.ReturnType.IsByRef || method.GetParameters().Any(parameter => parameter.ParameterType.IsByRef)))
            {
                fCanExecute = false;
                return node;
            }
            if (node is NewExpression creation && creation.Constructor != null && creation.Constructor.GetParameters().Any(parameter => parameter.ParameterType.IsByRef))
            {
                fCanExecute = false;
                return node;
            }
            switch (node.NodeType)
            {
                case ExpressionType.New when fAllowProjection:
                case ExpressionType.MemberInit when fAllowProjection:
                case ExpressionType.Constant:
                case ExpressionType.MemberAccess:
                case ExpressionType.Call:
                case ExpressionType.Conditional:
                case ExpressionType.AndAlso:
                case ExpressionType.OrElse:
                case ExpressionType.And:
                case ExpressionType.Or:
                case ExpressionType.ExclusiveOr:
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.Add:
                case ExpressionType.AddChecked:
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                case ExpressionType.Multiply:
                case ExpressionType.MultiplyChecked:
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.Power:
                case ExpressionType.Coalesce:
                case ExpressionType.LeftShift:
                case ExpressionType.RightShift:
                case ExpressionType.ArrayIndex:
                case ExpressionType.ArrayLength:
                case ExpressionType.Not:
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.UnaryPlus:
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                case ExpressionType.TypeAs:
                case ExpressionType.TypeIs:
                case ExpressionType.TypeEqual:
                case ExpressionType.IsTrue:
                case ExpressionType.IsFalse:
                case ExpressionType.Default:
                    return base.Visit(node);
                default:
                    fCanExecute = false;
                    return node;
            }
        }

        /// <summary>Reads only mapped immutable values and selects the translation without changing CurrentLanguage.</summary>
        /// <param name="node">Access to an entity property or a caller-supplied value.</param>
        /// <returns>The stored value with its optional fallback, or the original access.</returns>
        protected override Expression VisitMember(MemberExpression node)
        {
            if (ReferenceEquals(node.Expression, fParameter))
            {
                if (node.Member is PropertyInfo && IsImmutable(node.Type) && fContractProperties.TryGetValue(node.Member.Name, out PropertyInfo? property))
                {
                    string name = property.IsDefined(typeof(Multilingual)) ? LanguageColumns.getPropertyName(property.Name, fLanguage) : property.Name;
                    if (fStoredProperties.TryGetValue(name, out IProperty? stored) && stored.PropertyInfo != null)
                    {
                        Expression entity = Expression.Convert(fEntity, fConcreteType);
                        Expression translation = Expression.Property(entity, stored.PropertyInfo);
                        if (fLanguage != fDefaultLanguage && property.IsDefined(typeof(FallbackToDefaultLanguage)))
                        {
                            IProperty fallback = fStoredProperties[LanguageColumns.getPropertyName(property.Name, fDefaultLanguage)];
                            return Expression.Condition(Expression.Call(typeof(string), nameof(string.IsNullOrEmpty), Type.EmptyTypes, translation), Expression.Property(entity, fallback.PropertyInfo!), translation);
                        }
                        return translation;
                    }
                }
                fCanExecute = false;
                return node;
            }
            return base.VisitMember(node);
        }

        /// <summary>Limits values that may escape the shared cache to known immutable types.</summary>
        /// <param name="type">The type of a stored property.</param>
        /// <returns>True for simple values, strings, and their nullable variants.</returns>
        private static bool IsImmutable(Type type)
        {
            Type valueType = Nullable.GetUnderlyingType(type) ?? type;
            return valueType.IsPrimitive || valueType.IsEnum || valueType == typeof(string) || valueType == typeof(decimal) || valueType == typeof(DateTime) || valueType == typeof(DateTimeOffset) || valueType == typeof(TimeSpan) || valueType == typeof(DateOnly) || valueType == typeof(TimeOnly) || valueType == typeof(Guid);
        }
    }
}
