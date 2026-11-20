// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Blueprint.Database.Sql.Triggers
{
    /// <summary>Translates the same deterministic filter for inserted, deleted and existing source rows.</summary>
    internal static class CountPredicate
    {
        /// <summary>Renders comparisons and Boolean operators over mapped columns and literal values.</summary>
        /// <param name="expression">The predicate body or a Boolean subexpression.</param>
        /// <param name="row">The source lambda parameter.</param>
        /// <param name="column">Resolves a CLR property to a delimited column reference.</param>
        /// <returns>A SQL predicate independent of the caller's culture and connection settings.</returns>
        /// <exception cref="NotSupportedException">The expression is outside the supported predicate language.</exception>
        internal static string Translate(Expression expression, ParameterExpression row, Func<string, string> column)
        {
            if (expression is BinaryExpression binary)
            {
                if (binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
                {
                    string booleanOperator = binary.NodeType == ExpressionType.AndAlso ? "AND" : "OR";
                    return $"({Translate(binary.Left, row, column)} {booleanOperator} {Translate(binary.Right, row, column)})";
                }
                string comparison = binary.NodeType switch
                {
                    ExpressionType.Equal => "=",
                    ExpressionType.NotEqual => "<>",
                    ExpressionType.GreaterThan => ">",
                    ExpressionType.GreaterThanOrEqual => ">=",
                    ExpressionType.LessThan => "<",
                    ExpressionType.LessThanOrEqual => "<=",
                    _ => throw new NotSupportedException($"Unsupported count trigger comparison: {binary.NodeType}.")
                };
                return $"({Operand(binary.Left, row, column)} {comparison} {Operand(binary.Right, row, column)})";
            }
            if (expression is UnaryExpression { NodeType: ExpressionType.Not } negation)
            {
                return $"(NOT {Translate(negation.Operand, row, column)})";
            }
            return $"({Operand(expression, row, column)} = 1)";
        }

        /// <summary>Accepts only stored non-nullable scalar properties or integral literal constants.</summary>
        /// <param name="expression">The expression on one side of a comparison.</param>
        /// <param name="row">The source lambda parameter.</param>
        /// <param name="column">Resolves a mapped property.</param>
        /// <returns>A column reference or an invariant numeric literal.</returns>
        private static string Operand(Expression expression, ParameterExpression row, Func<string, string> column)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert } cast && cast.Method == null)
            {
                Type original = cast.Operand.Type.IsEnum ? Enum.GetUnderlyingType(cast.Operand.Type) : cast.Operand.Type;
                bool promoteToInt = cast.Type == typeof(int) && (original == typeof(byte) || original == typeof(short));
                bool promoteToLong = cast.Type == typeof(long) && (original == typeof(byte) || original == typeof(short) || original == typeof(int));
                if (cast.Type != original && !promoteToInt && !promoteToLong)
                {
                    break;
                }
                expression = cast.Operand;
            }
            Type type = expression.Type.IsEnum ? Enum.GetUnderlyingType(expression.Type) : expression.Type;
            if (type == typeof(bool) || type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long))
            {
                if (expression is MemberExpression { Member: PropertyInfo property } member && member.Expression == row)
                {
                    return column(property.Name);
                }
                if (expression is ConstantExpression literal)
                {
                    return Convert.ToInt64(literal.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                }
            }
            throw new NotSupportedException($"Count trigger filters require non-nullable Boolean, enum or integer properties and literal constants. Unsupported operand: {expression}.");
        }
    }
}
