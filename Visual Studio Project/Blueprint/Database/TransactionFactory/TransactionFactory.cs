// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace Blueprint.Database.TransactionFactory
{
    /// <summary>Creates and executes transactions for the same database with an exclusive pooled context and automatic commit or rollback.</summary>
    /// <remarks>Each BlueprintDatabase owns an instance. Each execution reserves its own context; synchronous callback queries reuse that context.</remarks>
    public sealed class TransactionFactory
    {
        /// <summary>Creates a factory that resolves the active database when an operation is requested.</summary>
        internal TransactionFactory()
        {
        }

        /// <summary>Creates an empty operation transaction after validating the active database.</summary>
        /// <returns>The transaction in which the caller registers inserts, updates, and deletes.</returns>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        /// <exception cref="InvalidOperationException">The database context has not been initialized.</exception>
        public DatabaseTransaction CreateNewTransaction()
        {
            BlueprintDatabase database = BlueprintDatabase.ACTIVE_INSTANCE;
            return database.ExecuteOperation(context => new DatabaseTransaction());
        }

        /// <summary>Creates a transaction that reads data and prepares its operation list within the same SQL isolation scope.</summary>
        /// <param name="prepare">A synchronous callback for queries and AddInsert/AddUpdate/AddDelete/AddValidation, before writes.</param>
        /// <returns>The new transaction for this database.</returns>
        /// <remarks>ExecuteTransactionAsync runs the callback on its reserved context, without direct CRUD or asynchronous work. Entity values are captured for rollback after preparation; the consumer is responsible for its own earlier changes.</remarks>
        /// <exception cref="ArgumentNullException">No callback was provided.</exception>
        /// <exception cref="ArgumentException">The callback is async void.</exception>
        /// <exception cref="ObjectDisposedException">The database has been disposed.</exception>
        /// <exception cref="InvalidOperationException">The context has not been initialized.</exception>
        public DatabaseTransaction CreateNewTransaction(Action<DatabaseTransaction> prepare)
        {
            ArgumentNullException.ThrowIfNull(prepare);
            BlueprintDatabase database = BlueprintDatabase.ACTIVE_INSTANCE;
            return database.ExecuteOperation(context => new DatabaseTransaction(prepare));
        }

        /// <summary>Submits operations once for atomic execution on a worker thread with automatic commit or rollback.</summary>
        /// <param name="transaction">The transaction containing operations in execution order.</param>
        /// <param name="isolationLevel">The SQL transaction isolation level.</param>
        /// <param name="cancellationToken">A cancellation request checked before operations and before commit.</param>
        /// <returns>The total number of write operations committed.</returns>
        /// <remarks>
        /// One context is reserved for the entire transaction, including preparation and validation queries on the worker thread.
        /// SQL executes synchronously on the worker while the consumer awaits asynchronously. Cancellation is checked between SQL commands.
        /// Calls from an existing database operation or from a TransactionScope are not allowed. The caller must not modify entities until the Task completes.
        /// Rollback restores the supplied entities' mapped values, including IDs, foreign keys, and database-generated values.
        /// Validations run after all operations, before the final cancellation check and commit. An exception triggers rollback.
        /// </remarks>
        /// <exception cref="ArgumentNullException">No transaction was provided.</exception>
        /// <exception cref="ArgumentException">An entity does not belong to the model or has an ID incompatible with the operation.</exception>
        /// <exception cref="InvalidOperationException">No database is active, the transaction has already been submitted, or an existing operation or external transaction prevents execution.</exception>
        /// <exception cref="ObjectDisposedException">The database was disposed before execution.</exception>
        /// <exception cref="OperationCanceledException">Cancellation was requested before commit was confirmed.</exception>
        /// <exception cref="DbUpdateException">A write or concurrency check failed.</exception>
        /// <exception cref="AggregateException">Rollback or value restoration also failed after the original error.</exception>
        /// <exception cref="System.Data.Common.DbException">The database provider failed.</exception>
        /// <exception cref="Exception">A preparation or validation callback failed; the error is propagated after the rollback attempt.</exception>
        public Task<int> ExecuteTransactionAsync(DatabaseTransaction transaction, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            BlueprintDatabase database = BlueprintDatabase.ACTIVE_INSTANCE;
            if (database.IsExecutingOperation || Monitor.IsEntered(database.SyncRoot) || System.Transactions.Transaction.Current != null)
            {
                throw new InvalidOperationException("Submit a database transaction outside an existing database operation or TransactionScope.");
            }
            transaction.BeginExecution();
            return Task.Run(() =>
            {
                bool isCommitted = false;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return database.ExecuteOperation(context =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (context.Database.CurrentTransaction != null)
                        {
                            throw new InvalidOperationException("The database already has an active transaction.");
                        }
                        Dictionary<BlueprintEntity, Dictionary<IProperty, object?>> snapshots = new Dictionary<BlueprintEntity, Dictionary<IProperty, object?>>(ReferenceEqualityComparer.Instance);
                        using (IDbContextTransaction sqlTransaction = context.Database.BeginTransaction(isolationLevel))
                        {
                            try
                            {
                                (IReadOnlyList<DatabaseTransaction.Operation> operations, IReadOnlyList<Action> validations) = transaction.PrepareExecution();
                                cancellationToken.ThrowIfCancellationRequested();
                                snapshots = CaptureTransactionValues(context, operations);
                                int affectedRows = 0;
                                foreach (DatabaseTransaction.Operation operation in operations)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    affectedRows += ExecuteTransactionOperation(context, operation);
                                }
                                foreach (Action validation in validations)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    validation();
                                }
                                cancellationToken.ThrowIfCancellationRequested();
                                sqlTransaction.Commit();
                                isCommitted = true;
                                return affectedRows;
                            }
                            catch (Exception exception)
                            {
                                RollbackTransaction(sqlTransaction, snapshots, exception);
                                throw;
                            }
                            finally
                            {
                                context.ChangeTracker.Clear();
                                database.InvalidateCache();
                            }
                        }
                    });
                }
                finally
                {
                    transaction.CompleteExecution(isCommitted);
                }
            });
        }

        /// <summary>Validates generated types and captures each distinct instance's original mapped values for rollback.</summary>
        /// <param name="context">The active model context.</param>
        /// <param name="operations">The fixed snapshot of operations.</param>
        /// <returns>The original values, comparing entities by reference.</returns>
        /// <exception cref="ArgumentException">An entity was not created by the active model.</exception>
        private static Dictionary<BlueprintEntity, Dictionary<IProperty, object?>> CaptureTransactionValues(BlueprintDatabaseContext context, IReadOnlyList<DatabaseTransaction.Operation> operations)
        {
            Dictionary<BlueprintEntity, Dictionary<IProperty, object?>> snapshots = new Dictionary<BlueprintEntity, Dictionary<IProperty, object?>>(ReferenceEqualityComparer.Instance);
            foreach (DatabaseTransaction.Operation operation in operations)
            {
                BlueprintEntity entity = operation.Entity;
                if (snapshots.ContainsKey(entity))
                {
                    continue;
                }
                Type concreteType = entity.GetType();
                if (!context.EntityTypes.Types.Values.Contains(concreteType))
                {
                    throw new ArgumentException("Transaction entities must be created by the registered entity model.", nameof(operations));
                }
                IEntityType entityType = context.Model.FindEntityType(concreteType)!;
                Dictionary<IProperty, object?> values = new Dictionary<IProperty, object?>();
                foreach (IProperty property in entityType.GetProperties())
                {
                    object? value = property.PropertyInfo!.GetValue(entity);
                    values.Add(property, value is byte[] bytes ? bytes.ToArray() : value);
                }
                snapshots.Add(entity, values);
            }
            return snapshots;
        }

        /// <summary>Executes one explicit operation without automatically saving an entire graph and clears tracking before the next operation.</summary>
        /// <param name="context">The context with the shared transaction.</param>
        /// <param name="operation">The operation to execute in registration order.</param>
        /// <returns>The number of affected rows.</returns>
        /// <exception cref="ArgumentException">The ID is incompatible with the operation.</exception>
        /// <exception cref="InvalidOperationException">A reference has not been persisted or has an incompatible foreign key.</exception>
        /// <exception cref="DbUpdateException">The write failed or the target row no longer exists.</exception>
        private static int ExecuteTransactionOperation(BlueprintDatabaseContext context, DatabaseTransaction.Operation operation)
        {
            BlueprintEntity entity = operation.Entity;
            IEntityType entityType = context.Model.FindEntityType(entity.GetType())!;
            try
            {
                if (operation.Type == DatabaseTransaction.EOperationType.Insert)
                {
                    if (entity.ID != 0)
                    {
                        throw new ArgumentException("New entities must have ID = 0.", nameof(operation));
                    }
                    ResolveTransactionReferences(entity, entityType);
                    context.Entry(entity).State = EntityState.Added;
                    return context.SaveChanges();
                }
                if (entity.ID <= 0)
                {
                    throw new ArgumentException("Update and Delete require a stored entity with a positive ID.", nameof(operation));
                }
                object stored = context.Find(entity.GetType(), entity.ID) ?? throw new DbUpdateConcurrencyException("The entity to update or delete no longer exists.");
                EntityEntry entry = context.Entry(stored);
                if (operation.Type == DatabaseTransaction.EOperationType.Update)
                {
                    ResolveTransactionReferences(entity, entityType);
                    entry.CurrentValues.SetValues(entity);
                }
                foreach (IProperty property in entityType.GetProperties().Where(property => property.IsConcurrencyToken))
                {
                    entry.Property(property.Name).OriginalValue = property.PropertyInfo!.GetValue(entity);
                }
                entry.State = operation.Type == DatabaseTransaction.EOperationType.Update ? EntityState.Modified : EntityState.Deleted;
                int affectedRows = context.SaveChanges();
                if (operation.Type == DatabaseTransaction.EOperationType.Update)
                {
                    foreach (IProperty property in entityType.GetProperties().Where(property => property.ValueGenerated != ValueGenerated.Never))
                    {
                        property.PropertyInfo!.SetValue(entity, property.PropertyInfo.GetValue(stored));
                    }
                }
                return affectedRows;
            }
            finally
            {
                context.ChangeTracker.Clear();
            }
        }

        /// <summary>Populates foreign keys from explicit references to already persisted parents, preserving batch order.</summary>
        /// <param name="entity">The entity before insertion or update.</param>
        /// <param name="entityType">The entity mapping.</param>
        /// <exception cref="InvalidOperationException">A parent has no ID or the reference conflicts with the declared foreign key.</exception>
        private static void ResolveTransactionReferences(BlueprintEntity entity, IEntityType entityType)
        {
            foreach (IForeignKey foreignKey in entityType.GetForeignKeys())
            {
                if (foreignKey.DependentToPrincipal?.PropertyInfo?.GetValue(entity) is not BlueprintEntity principal)
                {
                    continue;
                }
                if (principal.ID <= 0)
                {
                    throw new InvalidOperationException("Referenced entities must be inserted before their dependents.");
                }
                for (int index = 0; index < foreignKey.Properties.Count; index++)
                {
                    IProperty property = foreignKey.Properties[index];
                    object? currentValue = property.PropertyInfo!.GetValue(entity);
                    object? principalValue = foreignKey.PrincipalKey.Properties[index].PropertyInfo!.GetValue(principal);
                    if (currentValue != null && !Equals(currentValue, 0L) && !Equals(currentValue, principalValue))
                    {
                        throw new InvalidOperationException("The foreign key does not match the referenced entity.");
                    }
                    property.PropertyInfo.SetValue(entity, principalValue);
                }
            }
        }

        /// <summary>Attempts to roll back all writes and restores entity values, preserving additional errors.</summary>
        /// <param name="transaction">The actual database transaction.</param>
        /// <param name="snapshots">The values before the first operation.</param>
        /// <param name="originalException">The original execution error.</param>
        /// <exception cref="AggregateException">Rollback or restoration of a property also failed.</exception>
        private static void RollbackTransaction(IDbContextTransaction transaction, Dictionary<BlueprintEntity, Dictionary<IProperty, object?>> snapshots, Exception originalException)
        {
            List<Exception> failures = new List<Exception> { originalException };
            try
            {
                transaction.Rollback();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            foreach (KeyValuePair<BlueprintEntity, Dictionary<IProperty, object?>> snapshot in snapshots)
            {
                foreach (KeyValuePair<IProperty, object?> value in snapshot.Value)
                {
                    try
                    {
                        value.Key.PropertyInfo!.SetValue(snapshot.Key, value.Value);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }
            if (failures.Count > 1)
            {
                throw new AggregateException("The transaction failed and rollback encountered additional errors.", failures);
            }
        }
    }
}
