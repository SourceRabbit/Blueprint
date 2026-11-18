using Blueprint.Database.Sql.RAMCache;
using Blueprint.Entities;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Blueprint.Database.Queries.QueryExecutors.RAM
{
    /// <summary>Shares compiled selections for the lifetime of a cached table while binding each execution's own arguments.</summary>
    internal sealed class CachedPagePlans
    {
        private const int MAX_PLANS_PER_TABLE = 256;
        private static readonly ConditionalWeakTable<RAMCachedTable, CachedPagePlans> fTables = new();
        private static readonly ParameterExpression fArguments = Expression.Parameter(typeof(object[]), "arguments");
        private readonly Dictionary<Expression, PagePlan> fPlans = new(ExpressionEqualityComparer.Instance);
        private readonly Queue<Expression> fInsertionOrder = new();

        /// <summary>Filters or orders the supplied snapshot using the current execution's arguments.</summary>
        /// <param name="rows">The current table rows.</param>
        /// <param name="arguments">The original constants and closure objects for this execution.</param>
        /// <returns>The selected sequence in query order.</returns>
        private delegate IEnumerable<BlueprintEntity> PagePlan(IEnumerable<BlueprintEntity> rows, object?[] arguments);

        /// <summary>Binds a validated selection to its caller's constants after rewriting multilingual property access.</summary>
        /// <param name="table">The table whose model supplied the mapped properties.</param>
        /// <param name="rows">The source parameter of the selection.</param>
        /// <param name="source">The translated Enumerable pipeline.</param>
        /// <returns>A selection that uses the caller's values and the rows supplied at enumeration.</returns>
        internal static Func<IEnumerable<BlueprintEntity>, IEnumerable<BlueprintEntity>> Bind(RAMCachedTable table, ParameterExpression rows, Expression source)
        {
            PageArguments arguments = new PageArguments();
            Expression body = arguments.Visit(source);
            Expression<PagePlan> template = Expression.Lambda<PagePlan>(body, rows, fArguments);
            CachedPagePlans cache = fTables.GetValue(table, static _ => new CachedPagePlans());
            PagePlan plan = cache.GetOrCompile(template);
            object?[] values = arguments.fValues.ToArray();
            return currentRows => plan(currentRows, values);
        }

        /// <summary>Compiles each resident shape once and evicts the oldest shape when the table's cache is full.</summary>
        /// <param name="template">The selection with typed argument slots in place of caller constants.</param>
        /// <returns>The shared executable selection.</returns>
        private PagePlan GetOrCompile(Expression<PagePlan> template)
        {
            lock (fPlans)
            {
                if (fPlans.TryGetValue(template, out PagePlan? plan))
                {
                    return plan;
                }
                plan = template.Compile();
                if (fPlans.Count == MAX_PLANS_PER_TABLE)
                {
                    fPlans.Remove(fInsertionOrder.Dequeue());
                }
                fPlans.Add(template, plan);
                fInsertionOrder.Enqueue(template);
                return plan;
            }
        }

        /// <summary>Separates literal and captured inputs from the expression used as a structural cache key.</summary>
        private sealed class PageArguments : ExpressionVisitor
        {
            internal readonly List<object?> fValues = new();

            /// <summary>Replaces the constant with its typed argument slot, leaving closure member reads deferred.</summary>
            /// <param name="node">The original input, including nulls, comparers and captured objects.</param>
            /// <returns>A typed read from the current execution's argument array.</returns>
            protected override Expression VisitConstant(ConstantExpression node)
            {
                int index = fValues.Count;
                fValues.Add(node.Value);
                return Expression.Convert(Expression.ArrayIndex(fArguments, Expression.Constant(index)), node.Type);
            }
        }
    }
}
