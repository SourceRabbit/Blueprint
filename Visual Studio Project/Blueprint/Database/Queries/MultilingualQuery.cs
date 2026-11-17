using Blueprint.Database.Entities.Annotations;
using Blueprint.Database.Entities;
using Blueprint.Localization;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;

namespace Blueprint.Database.Queries
{
    /// <summary>Translates multilingual reads into stored columns with optional default-language fallback.</summary>
    internal sealed class MultilingualQuery : ExpressionVisitor
    {
        private readonly long fLanguageID;
        private readonly long fDefaultLanguageID;

        /// <summary>Selects one fixed language for the entire LINQ composition.</summary>
        /// <param name="language">The column language.</param>
        /// <param name="defaultLanguage">The language with an empty prefix.</param>
        internal MultilingualQuery(long language, long defaultLanguage)
        {
            fLanguageID = language;
            fDefaultLanguageID = defaultLanguage;
        }

        /// <summary>Replaces only accesses on entity parameters, preserving values from captured instances.</summary>
        /// <param name="node">The property access.</param>
        /// <returns>The translation with its optional fallback, or the original access.</returns>
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != null && typeof(BlueprintEntity).IsAssignableFrom(node.Expression.Type))
            {
                PropertyInfo? property = BlueprintEntityTypes.GetProperties(node.Expression.Type).FirstOrDefault(property => property.Name == node.Member.Name);
                if (property?.IsDefined(typeof(Multilingual)) == true && HasParameter(node.Expression))
                {
                    Expression entity = Visit(node.Expression);
                    Expression translation = Expression.Call(typeof(EF), nameof(EF.Property), new[] { property.PropertyType }, entity, Expression.Constant(LanguageColumns.getPropertyName(property.Name, fLanguageID)));
                    if (fLanguageID != fDefaultLanguageID && property.IsDefined(typeof(FallbackToDefaultLanguage)))
                    {
                        Expression fallback = Expression.Call(typeof(EF), nameof(EF.Property), new[] { property.PropertyType }, entity, Expression.Constant(LanguageColumns.getPropertyName(property.Name, fDefaultLanguageID)));
                        // DATALENGTH preserves whitespace-only translations, unlike SQL equality or LEN.
                        Expression length = Expression.Call(typeof(SqlServerDbFunctionsExtensions), nameof(SqlServerDbFunctionsExtensions.DataLength), Type.EmptyTypes, Expression.Property(null, typeof(EF), nameof(EF.Functions)), translation);
                        Expression missing = Expression.OrElse(Expression.Equal(translation, Expression.Constant(null, typeof(string))), Expression.Equal(length, Expression.Constant((int?)0, typeof(int?))));
                        return Expression.Condition(missing, fallback, translation);
                    }
                    return translation;
                }
            }
            return base.VisitMember(node);
        }

        /// <summary>Distinguishes expression member access from the caller's captured objects.</summary>
        /// <param name="expression">The access receiver.</param>
        /// <returns>Whether the access originates from a LINQ parameter.</returns>
        private static bool HasParameter(Expression expression)
        {
            return expression is ParameterExpression || expression is MemberExpression member && member.Expression != null && HasParameter(member.Expression) || expression is UnaryExpression unary && HasParameter(unary.Operand);
        }
    }
}
