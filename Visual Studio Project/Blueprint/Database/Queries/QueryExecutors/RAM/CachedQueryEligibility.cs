// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities.Annotations;
using Blueprint.Entities;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;

namespace Blueprint.Database.Queries.QueryExecutors.RAM
{
    /// <summary>Allows cached include graphs while keeping unsupported relationships, providers, and EF functions on SQL.</summary>
    internal sealed class CachedQueryEligibility : ExpressionVisitor
    {
        private readonly IQueryProvider fProvider;
        private readonly Type fContract;
        private readonly HashSet<ParameterExpression> fRootParameters = new HashSet<ParameterExpression>();
        private bool fCanExecute = true;

        /// <summary>Identifies the root table and its query provider.</summary>
        /// <param name="contract">The root entity class.</param>
        /// <param name="provider">The root collection's provider.</param>
        private CachedQueryEligibility(Type contract, IQueryProvider provider)
        {
            fContract = contract;
            fProvider = provider;
        }

        /// <summary>Checks whether the query can run on independent cached entity graphs.</summary>
        /// <param name="contract">The entity class.</param>
        /// <param name="expression">The complete LINQ expression.</param>
        /// <param name="provider">The root collection's provider.</param>
        /// <param name="includeRelated">Whether the result requires AutoInclude relationships to be loaded.</param>
        /// <returns>True if in-memory execution is allowed.</returns>
        internal static bool CanExecute(Type contract, Expression expression, IQueryProvider provider, bool includeRelated)
        {
            CachedQueryEligibility visitor = new CachedQueryEligibility(contract, provider);
            visitor.Visit(expression);
            return visitor.fCanExecute;
        }

        /// <summary>Rejects compositions containing a different IQueryable root.</summary>
        /// <param name="node">The expression constant.</param>
        /// <returns>The original constant.</returns>
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is IQueryable query && !ReferenceEquals(query.Provider, fProvider))
            {
                fCanExecute = false;
            }
            return base.VisitConstant(node);
        }

        /// <summary>Allows cached AutoInclude paths while rejecting other relationships and unmapped values.</summary>
        /// <param name="node">The property or captured-query access.</param>
        /// <returns>The original access.</returns>
        protected override Expression VisitMember(MemberExpression node)
        {
            if (typeof(IQueryable).IsAssignableFrom(node.Type))
            {
                fCanExecute = false;
            }
            if (node.Expression != null && typeof(BlueprintEntity).IsAssignableFrom(node.Expression.Type))
            {
                if ((BlueprintEntityTypes.GetRelatedContract(node.Type) != null && !IsIncludedNavigation(node)) || BlueprintEntityTypes.GetProperties(node.Expression.Type).Any(property => property.Name == node.Member.Name && property.IsDefined(typeof(NotMappedAttribute), true)))
                {
                    fCanExecute = false;
                }
            }
            return base.VisitMember(node);
        }

        /// <summary>Checks that navigation access stays within the root's declared, cycle-limited include paths.</summary>
        /// <param name="node">The relationship access.</param>
        /// <returns>True when every step is automatically loaded in the cached graph.</returns>
        private bool IsIncludedNavigation(MemberExpression node)
        {
            Stack<MemberExpression> path = new Stack<MemberExpression>();
            Expression? current = node;
            while (current is MemberExpression member && member.Expression != null && typeof(BlueprintEntity).IsAssignableFrom(member.Expression.Type))
            {
                path.Push(member);
                current = member.Expression;
            }
            if (current is not ParameterExpression parameter || !fRootParameters.Contains(parameter))
            {
                return false;
            }
            HashSet<Type> ancestors = new HashSet<Type>();
            foreach (MemberExpression member in path)
            {
                Type contract = member.Expression!.Type;
                if (!ancestors.Add(contract) || !BlueprintEntityTypes.GetProperties(contract).Any(property => property.Name == member.Member.Name && property.IsDefined(typeof(AutoInclude), true)))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Recognizes root entity sequences before any projection or navigation changes their meaning.</summary>
        /// <param name="expression">The source of a LINQ operation.</param>
        /// <returns>True for the root and supported entity-preserving compositions.</returns>
        private bool IsRootSequence(Expression expression)
        {
            while (expression is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable) && call.Arguments.Count > 0)
            {
                if (call.Method.Name is not (nameof(Queryable.Where) or nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending) or nameof(Queryable.Skip) or nameof(Queryable.Take) or nameof(Queryable.Distinct) or nameof(Queryable.Reverse)))
                {
                    return false;
                }
                expression = call.Arguments[0];
            }
            return expression is ConstantExpression { Value: IQueryable query } && query.ElementType == fContract && ReferenceEquals(query.Provider, fProvider);
        }

        /// <summary>Recognizes root lambda parameters and rejects EF-specific or unsafe entity method calls.</summary>
        /// <param name="node">The method call.</param>
        /// <returns>The original call.</returns>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable) && node.Arguments.Count > 0 && IsRootSequence(node.Arguments[0]))
            {
                foreach (UnaryExpression argument in node.Arguments.OfType<UnaryExpression>())
                {
                    if (argument.Operand is LambdaExpression lambda && lambda.Parameters.Count == 1 && lambda.Parameters[0].Type == fContract)
                    {
                        fRootParameters.Add(lambda.Parameters[0]);
                    }
                }
            }
            string methodNamespace = node.Method.DeclaringType?.Namespace ?? string.Empty;
            if (methodNamespace.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || (node.Method.DeclaringType != typeof(Queryable) && node.Arguments.Any(argument => typeof(BlueprintEntity).IsAssignableFrom(argument.Type))))
            {
                fCanExecute = false;
            }
            return base.VisitMethodCall(node);
        }
    }
}
