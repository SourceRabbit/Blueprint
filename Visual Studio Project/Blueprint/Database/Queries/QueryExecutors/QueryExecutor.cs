using System.Linq.Expressions;
using Blueprint.Database.Entities;
using Blueprint.Database.Queries.QueryExecutors.RAM;
using Blueprint.Database.Queries.QueryExecutors.Database;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Blueprint.Database.Queries.QueryExecutors
{
    /// <summary>Defines query and relationship reads using the source selected by the entity base class.</summary>
    internal abstract class QueryExecutor
    {
        protected readonly BlueprintDatabase fDatabase;
        protected readonly Type fContract;
        protected readonly IQueryProvider fProvider;
        protected readonly long fLanguage;

        /// <summary>Stores the entity contract, database, provider and language shared by every executor.</summary>
        /// <param name="database">The database owning the query.</param>
        /// <param name="contract">The entity class to read.</param>
        /// <param name="provider">The provider that owns the original expression.</param>
        /// <param name="language">The selected language ID.</param>
        protected QueryExecutor(BlueprintDatabase database, Type contract, IQueryProvider provider, long language)
        {
            fDatabase = database;
            fContract = contract;
            fProvider = provider;
            fLanguage = language;
        }

        /// <summary>Selects the executor from the entity's inheritance, independently of its parent relationships.</summary>
        /// <param name="database">The database owning the query.</param>
        /// <param name="contract">The entity class to read.</param>
        /// <param name="provider">The original expression provider.</param>
        /// <param name="language">The selected language.</param>
        /// <returns>The database or RAM executor for this entity.</returns>
        internal static QueryExecutor Create(BlueprintDatabase database, Type contract, IQueryProvider provider, long language)
        {
            QueryExecutor_Database databaseExecutor = new QueryExecutor_Database(database, contract, provider, language);
            return typeof(BlueprintEntityRAM).IsAssignableFrom(contract) ? new QueryExecutor_RAM(database, contract, provider, language, databaseExecutor) : databaseExecutor;
        }

        /// <summary>Executes a terminal operation through this entity's source and its supported fallback.</summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="expression">The complete expression to execute.</param>
        /// <param name="result">The result when execution is supported, including null or zero.</param>
        /// <returns>True if the query executed; otherwise false. Execution failures propagate as exceptions.</returns>
        /// <exception cref="Exception">Query translation, synchronization, or execution fails.</exception>
        internal abstract bool TryExecute<TResult>(BlueprintDatabaseContext context, Expression expression, out TResult result);

        /// <summary>Materializes the complete result list through this entity's source and its supported fallback.</summary>
        /// <typeparam name="TElement">The type of each result.</typeparam>
        /// <param name="context">The active context, with the database lock already held.</param>
        /// <param name="expression">The complete sequence expression.</param>
        /// <param name="items">The results when execution is supported, including an empty list.</param>
        /// <returns>True if the query executed; otherwise false. Execution failures propagate as exceptions.</returns>
        /// <exception cref="Exception">Query translation, synchronization, or materialization fails.</exception>
        internal abstract bool TryExecuteList<TElement>(BlueprintDatabaseContext context, Expression expression, out List<TElement>? items);

        /// <summary>Reads independent scalar entities matching a relationship key through this executor's source.</summary>
        /// <param name="context">The active context under the database lock.</param>
        /// <param name="key">The primary or foreign key used by the relationship.</param>
        /// <param name="ids">The key values required by the parent entities.</param>
        /// <returns>The matching independent entities, without recursively loading relationships.</returns>
        /// <exception cref="Exception">Synchronization or database access fails.</exception>
        internal abstract List<BlueprintEntity> ReadRelated(BlueprintDatabaseContext context, IProperty key, long[] ids);

        /// <summary>Replaces the original query root with the selected executor's data source.</summary>
        protected sealed class EntityRoot : ExpressionVisitor
        {
            private readonly IQueryProvider fProvider;
            private readonly IQueryable fRoot;

            /// <summary>Connects the original provider to the source that will execute the expression.</summary>
            /// <param name="provider">The original entity query's provider.</param>
            /// <param name="root">The RAM or EF Core data source.</param>
            internal EntityRoot(IQueryProvider provider, IQueryable root)
            {
                fProvider = provider;
                fRoot = root;
            }

            /// <summary>Replaces only query roots that belong to the specified provider.</summary>
            /// <param name="node">The constant expression to examine.</param>
            /// <returns>The new root expression or the original constant.</returns>
            protected override Expression VisitConstant(ConstantExpression node)
            {
                return node.Value is IQueryable query && ReferenceEquals(query.Provider, fProvider) ? fRoot.Expression : base.VisitConstant(node);
            }
        }
    }
}
