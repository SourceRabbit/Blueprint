// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Blueprint.Database.Queries
{
    /// <summary>Translates Blueprint substring and prefix comparisons only when a query is executed by SQL Server.</summary>
    internal sealed class TextSearchQuery : ExpressionVisitor
    {
        private static readonly MethodInfo fContainsMethod = typeof(TextSearch).GetMethod(nameof(TextSearch.Contains), new[] { typeof(string), typeof(string), typeof(CompareOptions) })!;
        private static readonly MethodInfo fStartsWithMethod = typeof(TextSearch).GetMethod(nameof(TextSearch.StartsWith), new[] { typeof(string), typeof(string), typeof(CompareOptions) })!;

        /// <summary>Replaces each search overload with a null-safe SQL-translatable string operation under an explicit collation.</summary>
        /// <param name="node">The method call in the SQL query.</param>
        /// <returns>The translated search, or the visited original call for other methods.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The search options include an unsupported flag.</exception>
        /// <exception cref="NotSupportedException">The search options depend on a query row.</exception>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method != fContainsMethod && node.Method != fStartsWithMethod)
            {
                return base.VisitMethodCall(node);
            }

            CompareOptions options = getOptions(node.Arguments[2]);
            string collation = options switch
            {
                CompareOptions.None => "Latin1_General_100_CS_AS_SC",
                CompareOptions.IgnoreCase => "Latin1_General_100_CI_AS_SC",
                CompareOptions.IgnoreNonSpace => "Latin1_General_100_CS_AI_SC",
                _ => "Latin1_General_100_CI_AI_SC"
            };
            Expression source = Visit(node.Arguments[0]);
            Expression value = Visit(node.Arguments[1]);
            Expression functions = Expression.Property(null, typeof(EF), nameof(EF.Functions));
            Expression collated = Expression.Call(typeof(RelationalDbFunctionsExtensions), nameof(RelationalDbFunctionsExtensions.Collate), new[] { typeof(string) }, functions, source, Expression.Constant(collation));
            string methodName = node.Method == fStartsWithMethod ? nameof(string.StartsWith) : nameof(string.Contains);
            Expression matches = Expression.Call(collated, methodName, Type.EmptyTypes, value);
            Expression notNull = Expression.AndAlso(Expression.NotEqual(source, Expression.Constant(null, typeof(string))), Expression.NotEqual(value, Expression.Constant(null, typeof(string))));
            return Expression.AndAlso(notNull, matches);
        }

        /// <summary>Reads constant or captured comparison options before SQL translation because COLLATE requires a fixed name.</summary>
        /// <param name="expression">The comparison options expression, independent of query rows.</param>
        /// <returns>The validated options for this query execution.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The options include an unsupported flag.</exception>
        /// <exception cref="NotSupportedException">The options expression cannot be evaluated independently of a query row.</exception>
        private static CompareOptions getOptions(Expression expression)
        {
            if (expression is ConstantExpression { Value: CompareOptions constantOptions })
            {
                TextSearch.ValidateOptions(constantOptions);
                return constantOptions;
            }

            Func<CompareOptions> readOptions;
            try
            {
                readOptions = Expression.Lambda<Func<CompareOptions>>(expression).Compile(preferInterpretation: true);
            }
            catch (InvalidOperationException exception)
            {
                throw new NotSupportedException("Text search comparison options must be constant or captured values independent of query rows.", exception);
            }

            CompareOptions options = readOptions();
            TextSearch.ValidateOptions(options);
            return options;
        }
    }
}
