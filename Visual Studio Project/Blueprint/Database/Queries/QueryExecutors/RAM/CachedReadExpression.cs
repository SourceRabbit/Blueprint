// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities.Annotations;
using Blueprint.Entities;
using Blueprint.Database.Sql.RAMCache;
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
        private Dictionary<string, (CachedReadExpression Reader, Expression Row)>? fReferences;
        private Dictionary<Type, CachedReference>? fReferenceSnapshots;
        private BlueprintDatabaseContext? fSortContext;
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
        /// <param name="sortContext">The model used to resolve included RAM references in ordering keys.</param>
        /// <returns>True if shared mutable data cannot be modified or escape.</returns>
        internal bool TryRewriteLambda(Expression expression, out LambdaExpression? lambda, bool allowProjection = false, BlueprintDatabaseContext? sortContext = null)
        {
            lambda = null;
            if (expression is not UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression input } || input.Parameters.Count != 1 || input.Parameters[0].Type != fContract)
            {
                return false;
            }
            fParameter = input.Parameters[0];
            fSortContext = sortContext;
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
                Expression? value = ReadScalar(node.Member, fEntity);
                if (value != null)
                {
                    return value;
                }
                fCanExecute = false;
                return node;
            }
            if (node.Expression is MemberExpression reference && getReference(reference) is { } target && target.Reader.ReadScalar(node.Member, target.Row) is Expression scalar)
            {
                return scalar;
            }
            return base.VisitMember(node);
        }

        /// <summary>Tests whether an included reference exists in the same snapshot used to read its sorting value.</summary>
        /// <param name="node">The original comparison or binary operation.</param>
        /// <returns>The reference existence test or the normally visited expression.</returns>
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (fSortContext != null && node.Method == null && node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
            {
                Expression? operand = node.Right is ConstantExpression { Value: null } ? node.Left : node.Left is ConstantExpression { Value: null } ? node.Right : null;
                if (operand is MemberExpression reference && getReference(reference) is { } target)
                {
                    return Expression.MakeBinary(node.NodeType, target.Row, Expression.Constant(null, typeof(BlueprintEntity)));
                }
            }
            return base.VisitBinary(node);
        }

        /// <summary>Builds a read of an immutable mapped scalar, including the selected language and its fallback.</summary>
        /// <param name="member">The requested entity member.</param>
        /// <param name="row">The cached row providing the value.</param>
        /// <returns>The scalar read, or null for an unsupported member.</returns>
        private Expression? ReadScalar(MemberInfo member, Expression row)
        {
            if (member is not PropertyInfo property || !IsImmutable(property.PropertyType) || !fContractProperties.TryGetValue(member.Name, out PropertyInfo? mapped))
            {
                return null;
            }
            string name = mapped.IsDefined(typeof(Multilingual)) ? LanguageColumns.getPropertyName(mapped.Name, fLanguage) : mapped.Name;
            if (!fStoredProperties.TryGetValue(name, out IProperty? stored) || stored.PropertyInfo == null)
            {
                return null;
            }
            Expression entity = Expression.Convert(row, fConcreteType);
            Expression translation = Expression.Property(entity, stored.PropertyInfo);
            if (fLanguage != fDefaultLanguage && mapped.IsDefined(typeof(FallbackToDefaultLanguage)))
            {
                IProperty fallback = fStoredProperties[LanguageColumns.getPropertyName(mapped.Name, fDefaultLanguage)];
                return Expression.Condition(Expression.Call(typeof(string), nameof(string.IsNullOrEmpty), Type.EmptyTypes, translation), Expression.Property(entity, fallback.PropertyInfo!), translation);
            }
            return translation;
        }

        /// <summary>Resolves a direct included reference through its foreign key and a query-local snapshot of the target table.</summary>
        /// <param name="reference">The candidate navigation on the root parameter.</param>
        /// <returns>The target's scalar reader and row expression, or null for unsupported relationships.</returns>
        private (CachedReadExpression Reader, Expression Row)? getReference(MemberExpression reference)
        {
            if (fSortContext == null || reference.Expression != fParameter || !typeof(BlueprintEntityRAM).IsAssignableFrom(reference.Type))
            {
                return null;
            }
            string name = reference.Member.Name;
            if (fReferences != null && fReferences.TryGetValue(name, out (CachedReadExpression Reader, Expression Row) existing))
            {
                return existing;
            }
            INavigation? navigation = fSortContext.Model.FindEntityType(fConcreteType)?.FindNavigation(name);
            if (navigation is not { IsOnDependent: true, IsCollection: false } || !reference.Member.IsDefined(typeof(AutoInclude), true) ||
                !navigation.ForeignKey.PrincipalKey.IsPrimaryKey() || navigation.ForeignKey.Properties is not [{ PropertyInfo: { } foreignKey }] ||
                (foreignKey.PropertyType != typeof(long) && foreignKey.PropertyType != typeof(long?)))
            {
                return null;
            }
            RAMCachedTable? table = BlueprintDatabase.ACTIVE_INSTANCE.GetCachedTable(reference.Type, fSortContext);
            if (table == null)
            {
                return null;
            }
            fReferenceSnapshots ??= new Dictionary<Type, CachedReference>();
            if (!fReferenceSnapshots.TryGetValue(reference.Type, out CachedReference? snapshot))
            {
                snapshot = new CachedReference(table);
                fReferenceSnapshots.Add(reference.Type, snapshot);
            }
            Expression key = Expression.Property(Expression.Convert(fEntity, fConcreteType), foreignKey);
            Expression row = Expression.Call(Expression.Constant(snapshot), nameof(CachedReference.FindRow), Type.EmptyTypes, Expression.Convert(key, typeof(long?)));
            CachedReadExpression reader = new CachedReadExpression(reference.Type, table.ConcreteType, table.Properties, fLanguage, fDefaultLanguage);
            fReferences ??= new Dictionary<string, (CachedReadExpression Reader, Expression Row)>();
            fReferences.Add(name, (reader, row));
            return (reader, row);
        }

        /// <summary>Limits values that may escape the shared cache to known immutable types.</summary>
        /// <param name="type">The type of a stored property.</param>
        /// <returns>True for simple values, strings, and their nullable variants.</returns>
        private static bool IsImmutable(Type type)
        {
            Type valueType = Nullable.GetUnderlyingType(type) ?? type;
            return valueType.IsPrimitive || valueType.IsEnum || valueType == typeof(string) || valueType == typeof(decimal) || valueType == typeof(DateTime) || valueType == typeof(DateTimeOffset) || valueType == typeof(TimeSpan) || valueType == typeof(DateOnly) || valueType == typeof(TimeOnly) || valueType == typeof(Guid);
        }

        /// <summary>The reference snapshots used by ordering keys for this execution.</summary>
        internal IReadOnlyDictionary<Type, CachedReference>? ReferenceSnapshots
        {
            get
            {
                return fReferenceSnapshots;
            }
        }
    }
}
