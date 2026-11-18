using Blueprint.Database.Sql.RAMCache;
using Blueprint.Entities;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Blueprint.Database.Queries.QueryExecutors.RAM
{
    /// <summary>Selects candidates for a required equality or set membership on a cached table's primary key or a mapped foreign key.</summary>
    internal static class CachedKeyLookup
    {
        /// <summary>Narrows the source of a leading Where chain while retaining the original predicates, ordering and paging.</summary>
        /// <param name="table">The synchronized table that owns the rows and their indexes.</param>
        /// <param name="expression">The complete query being executed.</param>
        /// <param name="provider">The provider that owns the source table.</param>
        /// <returns>The indexed candidates, or all rows when narrowing would change query semantics.</returns>
        internal static IEnumerable<BlueprintEntity> SelectRows(RAMCachedTable table, Expression expression, IQueryProvider provider)
        {
            var key = FindRequiredKey(table, expression, provider);
            if (key == null)
            {
                return table.Rows.Values;
            }
            (IProperty property, long? id, IEnumerable<long?>? ids) = key.Value;
            if (ids != null)
            {
                return table.GetRowsByKeys(property, ids);
            }
            if (property.IsPrimaryKey())
            {
                return id.HasValue && table.Rows.TryGetValue(id.Value, out BlueprintEntity? row) ? new[] { row } : Array.Empty<BlueprintEntity>();
            }
            return table.GetForeignKeyRows(property, id);
        }

        /// <summary>Looks up at most one row when the filters require its ID and the projection cannot change those filters' inputs.</summary>
        /// <param name="table">The synchronized table.</param>
        /// <param name="filters">The entity source before Select.</param>
        /// <param name="selector">The projected expression.</param>
        /// <param name="provider">The query's owning provider.</param>
        /// <param name="row">The indexed row, or null when the key is missing.</param>
        /// <returns>True for a supported primary-key equality, including a lookup miss.</returns>
        internal static bool TryGetProjectedRow(RAMCachedTable table, Expression filters, Expression selector, IQueryProvider provider, out BlueprintEntity? row)
        {
            row = null;
            var key = FindRequiredKey(table, filters, provider);
            if (key == null || key.Value.Values != null || !key.Value.Property.IsPrimaryKey() || !IsStableProjection(selector))
            {
                return false;
            }
            if (key.Value.Value is long id)
            {
                table.Rows.TryGetValue(id, out row);
            }
            return true;
        }

        /// <summary>Accepts simple reads and built-in scalar computations that cannot mutate the captured key during enumeration.</summary>
        /// <param name="expression">The body of the selector or one of its operands.</param>
        /// <returns>True when applying the selector only to the indexed row preserves evaluation semantics.</returns>
        private static bool IsStableProjection(Expression expression)
        {
            if (expression is ConditionalExpression conditional)
            {
                return IsStableProjection(conditional.Test) && IsStableProjection(conditional.IfTrue) && IsStableProjection(conditional.IfFalse);
            }
            if (expression is BinaryExpression { NodeType: ExpressionType.Coalesce, Conversion: null } coalesce)
            {
                return IsStableProjection(coalesce.Left) && IsStableProjection(coalesce.Right);
            }
            if (expression is BinaryExpression binary && binary.NodeType is ExpressionType.Add or ExpressionType.Subtract or ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Modulo)
            {
                bool builtIn = binary.Method == null || binary.Method.DeclaringType == typeof(decimal) || (binary.Method.DeclaringType == typeof(string) && binary.Left.Type == typeof(string) && binary.Right.Type == typeof(string));
                return builtIn && IsStableProjection(binary.Left) && IsStableProjection(binary.Right);
            }
            return IsSimpleValue(expression);
        }

        /// <summary>Finds a required key in the leading filters of a safely narrowable query.</summary>
        /// <param name="table">The mapped table.</param>
        /// <param name="expression">The source query or its terminal expression.</param>
        /// <param name="provider">The provider that must own the root.</param>
        /// <returns>The selected equality or membership test, or null when all source rows are needed.</returns>
        private static (IProperty Property, long? Value, IEnumerable<long?>? Values)? FindRequiredKey(RAMCachedTable table, Expression expression, IQueryProvider provider)
        {
            Expression source = expression;
            (IProperty Property, long? Value, IEnumerable<long?>? Values)? key = null;
            while (source is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable))
            {
                if (call.Method.Name == nameof(Queryable.Where) && call.Arguments[1] is UnaryExpression { Operand: LambdaExpression filter } && filter.Parameters.Count == 1)
                {
                    FindKey(table, filter.Body, filter.Parameters[0], ref key);
                }
                else
                {
                    // A later Where cannot restrict the input of an earlier Skip, Take or projection.
                    key = null;
                }
                source = call.Arguments[0];
            }
            return key.HasValue && source is ConstantExpression { Value: IQueryable root } && ReferenceEquals(root.Provider, provider) && IsSimpleQuery(expression) ? key : null;
        }

        /// <summary>Finds required key tests within conjunctions, preferring a primary-key equality when available.</summary>
        /// <param name="table">The table providing the mapped key properties.</param>
        /// <param name="test">The filter or one of its conjuncts.</param>
        /// <param name="row">The root entity parameter.</param>
        /// <param name="key">The most selective usable key found so far.</param>
        private static void FindKey(RAMCachedTable table, Expression test, ParameterExpression row, ref (IProperty Property, long? Value, IEnumerable<long?>? Values)? key)
        {
            if (key.HasValue && key.Value.Values == null && key.Value.Property.IsPrimaryKey())
            {
                return;
            }
            if (test is BinaryExpression { NodeType: ExpressionType.AndAlso, Method: null } conjunction)
            {
                FindKey(table, conjunction.Left, row, ref key);
                FindKey(table, conjunction.Right, row, ref key);
                return;
            }
            if (test is MethodCallExpression call && TryReadContains(call, out Expression operand, out IEnumerable<long?>? ids))
            {
                IProperty? member = GetKeyProperty(table, operand, row);
                if (member != null && (!key.HasValue || (key.Value.Values != null && !key.Value.Property.IsPrimaryKey() && member.IsPrimaryKey())))
                {
                    key = (member, null, ids);
                }
                return;
            }
            if (test is not BinaryExpression { NodeType: ExpressionType.Equal, Method: null } equality)
            {
                return;
            }
            IProperty? property = GetKeyProperty(table, equality.Left, row);
            Expression input = equality.Right;
            if (property == null)
            {
                property = GetKeyProperty(table, equality.Right, row);
                input = equality.Left;
            }
            if (property != null && (!key.HasValue || key.Value.Values != null || property.IsPrimaryKey()) && TryReadValue(input, out object? value))
            {
                if (value is null or long or int or uint or short or ushort or byte or sbyte or char)
                {
                    key = (property, value == null ? null : Convert.ToInt64(value), null);
                }
            }
        }

        /// <summary>Reads standard Contains inputs with default ID equality without enumerating application-defined collections.</summary>
        /// <param name="call">The candidate membership test.</param>
        /// <param name="operand">The expression whose membership is tested.</param>
        /// <param name="ids">The current ID collection, including null for nullable foreign keys.</param>
        /// <returns>True for supported array, list and set membership tests.</returns>
        private static bool TryReadContains(MethodCallExpression call, out Expression operand, out IEnumerable<long?>? ids)
        {
            operand = call;
            ids = null;
            if (call.Method.Name != nameof(Enumerable.Contains))
            {
                return false;
            }
            Type? owner = call.Method.DeclaringType;
            Expression collection;
            if (owner == typeof(Enumerable) && call.Arguments.Count == 2)
            {
                collection = call.Arguments[0];
                operand = call.Arguments[1];
            }
            else if (call.Object != null && call.Arguments.Count == 1 && owner is { IsGenericType: true })
            {
                Type definition = owner.GetGenericTypeDefinition();
                if (definition != typeof(HashSet<>) && definition != typeof(List<>) && definition != typeof(ICollection<>) && definition != typeof(IReadOnlySet<>))
                {
                    return false;
                }
                collection = call.Object;
                operand = call.Arguments[0];
            }
            else
            {
                return false;
            }
            if (operand.Type != typeof(long) && operand.Type != typeof(long?))
            {
                return false;
            }
            if (collection is NewArrayExpression { NodeType: ExpressionType.NewArrayInit } array)
            {
                long?[] values = new long?[array.Expressions.Count];
                for (int index = 0; index < values.Length; index++)
                {
                    if (!TryReadValue(array.Expressions[index], out object? item))
                    {
                        return false;
                    }
                    values[index] = item == null ? null : Convert.ToInt64(item);
                }
                ids = values;
                return true;
            }
            if (!TryReadValue(collection, out object? value) || value == null)
            {
                return false;
            }
            Type type = value.GetType();
            if (type == typeof(long[]) || type == typeof(List<long>) || (type == typeof(HashSet<long>) && ReferenceEquals(((HashSet<long>)value).Comparer, EqualityComparer<long>.Default)))
            {
                ids = ((IEnumerable<long>)value).Select(id => (long?)id);
                return true;
            }
            if (type == typeof(long?[]) || type == typeof(List<long?>) || (type == typeof(HashSet<long?>) && ReferenceEquals(((HashSet<long?>)value).Comparer, EqualityComparer<long?>.Default)))
            {
                ids = (IEnumerable<long?>)value;
                return true;
            }
            return false;
        }

        /// <summary>Resolves a direct entity property, including a nullable lift, to a key in the table's model.</summary>
        /// <param name="table">The table mapped to the root entity.</param>
        /// <param name="operand">The key operand of an equality or membership test.</param>
        /// <param name="row">The filter's entity parameter.</param>
        /// <returns>The mapped key, or null for unrelated members and computed expressions.</returns>
        private static IProperty? GetKeyProperty(RAMCachedTable table, Expression operand, ParameterExpression row)
        {
            if (operand is UnaryExpression { NodeType: ExpressionType.Convert, Method: null } lift && lift.Type == typeof(long?))
            {
                operand = lift.Operand;
            }
            if (operand is MemberExpression { Member.Name: nameof(Nullable<long>.Value), Expression.Type: var type } nullable && type == typeof(long?))
            {
                operand = nullable.Expression;
            }
            return operand is MemberExpression { Member: PropertyInfo property } member && member.Expression == row ? table.getLookupKey(property.Name) : null;
        }

        /// <summary>Limits narrowing to one source and side-effect-free arguments so skipped rows cannot change captured values or suppress exceptions.</summary>
        /// <param name="expression">The query and its terminal operator, if any.</param>
        /// <returns>True when the original operations can safely run against the candidate sequence.</returns>
        private static bool IsSimpleQuery(Expression expression)
        {
            while (expression is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable))
            {
                if (call.Arguments.Count > 2 || call.Method.Name is not (
                    nameof(Queryable.Where) or nameof(Queryable.Select) or nameof(Queryable.Skip) or nameof(Queryable.Take) or
                    nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending) or
                    nameof(Queryable.Any) or nameof(Queryable.Count) or nameof(Queryable.LongCount) or
                    nameof(Queryable.First) or nameof(Queryable.FirstOrDefault) or nameof(Queryable.Single) or nameof(Queryable.SingleOrDefault)))
                {
                    return false;
                }
                if (call.Arguments.Count == 2 && !IsSimpleValue(call.Arguments[1]))
                {
                    return false;
                }
                expression = call.Arguments[0];
            }
            return true;
        }

        /// <summary>Accepts direct stored reads, stable captured inputs and nonthrowing built-in comparisons.</summary>
        /// <param name="expression">An operator argument or filter node.</param>
        /// <param name="guard">Earlier conditions known to be true on this branch.</param>
        /// <returns>False for arbitrary getters, callbacks, unguarded nullable dereferences and other observable operations.</returns>
        private static bool IsSimpleValue(Expression expression, Expression? guard = null)
        {
            switch (expression)
            {
                case UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression lambda }:
                    return lambda.Parameters.Count == 1 && IsSimpleValue(lambda.Body);
                case ConstantExpression constant:
                    return constant.Value is not IQueryable;
                case ParameterExpression:
                    return true;
                case MemberExpression { Expression: ParameterExpression parameter, Member: PropertyInfo property }:
                    return typeof(BlueprintEntity).IsAssignableFrom(parameter.Type) && (property.GetMethod?.IsAbstract == true || IsAutoGetter(property));
                case MemberExpression { Expression: MemberExpression member } access when member.Type == typeof(long?) && member.Expression is ParameterExpression:
                    return IsSimpleValue(member) && (access.Member.Name == nameof(Nullable<long>.HasValue) || (access.Member.Name == nameof(Nullable<long>.Value) && HasValueGuard(guard, member)));
                case MethodCallExpression call:
                    return TryReadContains(call, out Expression operand, out _) && IsSimpleValue(operand, guard);
                case NewExpression tuple when tuple.Type.Assembly == typeof(ValueTuple).Assembly && typeof(ITuple).IsAssignableFrom(tuple.Type) && tuple.Type.IsValueType && tuple.Arguments.Count <= 7:
                    return tuple.Arguments.All(argument => IsSimpleValue(argument, guard));
                case MemberExpression:
                    return TryReadValue(expression, out _);
                case UnaryExpression { NodeType: ExpressionType.Not, Method: null } unary:
                    return IsSimpleValue(unary.Operand, guard);
                case UnaryExpression conversion when IsWideningConversion(conversion):
                    return IsSimpleValue(conversion.Operand, guard);
                case BinaryExpression { NodeType: ExpressionType.AndAlso, Method: null } conjunction:
                    return IsSimpleValue(conjunction.Left, guard) && IsSimpleValue(conjunction.Right, guard == null ? conjunction.Left : Expression.AndAlso(guard, conjunction.Left));
                case BinaryExpression binary when binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.OrElse
                    or ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual:
                    return (binary.Method == null || binary.Method.DeclaringType == typeof(decimal) || binary.Method.DeclaringType == typeof(string)) && IsSimpleValue(binary.Left, guard) && IsSimpleValue(binary.Right, guard);
                default:
                    return false;
            }
        }

        /// <summary>Checks for a preceding HasValue test on the same row and nullable property.</summary>
        /// <param name="guard">The conditions already satisfied.</param>
        /// <param name="member">The nullable member being dereferenced.</param>
        /// <returns>True when reading Value is safe on this branch.</returns>
        private static bool HasValueGuard(Expression? guard, MemberExpression member)
        {
            if (guard is BinaryExpression { NodeType: ExpressionType.AndAlso, Method: null } conjunction)
            {
                return HasValueGuard(conjunction.Left, member) || HasValueGuard(conjunction.Right, member);
            }
            return guard is MemberExpression { Member.Name: nameof(Nullable<long>.HasValue), Expression: MemberExpression other } && other.Member == member.Member && other.Expression == member.Expression;
        }

        /// <summary>Reads a literal or captured input at execution time without invoking application-defined accessors.</summary>
        /// <param name="expression">The captured input expression.</param>
        /// <param name="value">The current value if it can be read safely.</param>
        /// <returns>False for unbound, null-intermediate or computed inputs.</returns>
        private static bool TryReadValue(Expression expression, out object? value)
        {
            value = null;
            if (expression is ConstantExpression constant)
            {
                value = constant.Value;
                return true;
            }
            if (expression is UnaryExpression conversion && IsWideningConversion(conversion))
            {
                return TryReadValue(conversion.Operand, out value);
            }
            if (expression is MemberExpression { Expression: not null } member && TryReadValue(member.Expression, out object? owner) && owner != null)
            {
                if (member.Member is FieldInfo field)
                {
                    value = field.GetValue(owner);
                    return true;
                }
                if (member.Member is PropertyInfo property && IsAutoGetter(property))
                {
                    value = property.GetValue(owner);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Recognizes conversions that preserve the input value in numeric and enum comparisons.</summary>
        /// <param name="conversion">The candidate unary expression.</param>
        /// <returns>True for a nullable lift, a lossless integral conversion to long, or an enum conversion to its backing type.</returns>
        private static bool IsWideningConversion(UnaryExpression conversion)
        {
            if (conversion.NodeType != ExpressionType.Convert || conversion.Method != null)
            {
                return false;
            }
            Type from = conversion.Operand.Type;
            Type to = conversion.Type;
            return Nullable.GetUnderlyingType(to) == from || (from.IsEnum && Enum.GetUnderlyingType(from) == to) ||
                (to == typeof(long) && (from == typeof(int) || from == typeof(uint) || from == typeof(short) || from == typeof(ushort) || from == typeof(byte) || from == typeof(sbyte) || from == typeof(char)));
        }

        /// <summary>Checks that a captured property uses an automatic getter with no overridable dispatch.</summary>
        /// <param name="property">The property being read.</param>
        /// <returns>True for a compiler-generated getter whose implementation is fixed.</returns>
        private static bool IsAutoGetter(PropertyInfo property)
        {
            MethodInfo? getter = property.GetMethod;
            return getter != null && (!getter.IsVirtual || getter.IsFinal || property.DeclaringType!.IsSealed) && getter.IsDefined(typeof(CompilerGeneratedAttribute), false);
        }
    }
}
