using Blueprint.Entities;
using System.Runtime.CompilerServices;

namespace Blueprint.Database.TransactionFactory
{
    /// <summary>Collects operations in registration order for atomic execution by the database's TransactionFactory.</summary>
    /// <remarks>
    /// This class only registers operations; it does not access the database or execute commit or rollback.
    /// Registration and reading of the list are synchronized across threads.
    /// Entities are retained by reference and must not be modified by the caller while the transaction is executing.
        /// Each instance is submitted once, even if execution fails or is cancelled. Validations run after the operations and before commit.
    /// </remarks>
    public sealed class DatabaseTransaction
    {
        private readonly object fSyncRoot;
        private readonly List<Operation> fOperations;
        private readonly List<Action> fValidations;
        private readonly Action<DatabaseTransaction>? fPrepare;
        private int fPreparationThreadID;
        private bool fExecutionStarted;
        private bool fIsCompleted;
        private bool fIsCommitted;

        /// <summary>Creates an empty operation list without opening a database connection or transaction.</summary>
        public DatabaseTransaction()
        {
            fSyncRoot = new object();
            fOperations = new List<Operation>();
            fValidations = new List<Action>();
        }

        /// <summary>Registers synchronous preparation of operations within the active database's SQL transaction.</summary>
        /// <param name="prepare">Synchronous preparation of the list within the SQL transaction.</param>
        /// <exception cref="ArgumentNullException">No callback was provided.</exception>
        /// <exception cref="ArgumentException">The callback is async void.</exception>
        internal DatabaseTransaction(Action<DatabaseTransaction> prepare) : this()
        {
            ArgumentNullException.ThrowIfNull(prepare);
            ValidateCallback(prepare);
            fPrepare = prepare;
        }

        /// <summary>Indicates whether the submitted execution finished, successfully or with failure.</summary>
        public bool IsCompleted
        {
            get
            {
                lock (fSyncRoot)
                {
                    return fIsCompleted;
                }
            }
        }

        /// <summary>Indicates whether the provider confirmed commit of all operations.</summary>
        public bool IsCommitted
        {
            get
            {
                lock (fSyncRoot)
                {
                    return fIsCommitted;
                }
            }
        }

        /// <summary>Returns a snapshot of the operation order without allowing the caller to modify the list.</summary>
        public IReadOnlyList<Operation> Operations
        {
            get
            {
                lock (fSyncRoot)
                {
                    return Array.AsReadOnly(fOperations.ToArray());
                }
            }
        }

        /// <summary>Registers an entity insert for future execution without saving it or changing its ID.</summary>
        /// <param name="entity">The entity to be inserted.</param>
        /// <exception cref="ArgumentNullException">No entity was provided.</exception>
        /// <exception cref="InvalidOperationException">The transaction has already been submitted.</exception>
        public void AddInsert(BlueprintEntity entity)
        {
            AddOperation(EOperationType.Insert, entity);
        }

        /// <summary>Registers an entity update after the operations already queued.</summary>
        /// <param name="entity">The entity containing the values to update.</param>
        /// <exception cref="ArgumentNullException">No entity was provided.</exception>
        /// <exception cref="InvalidOperationException">The transaction has already been submitted.</exception>
        public void AddUpdate(BlueprintEntity entity)
        {
            AddOperation(EOperationType.Update, entity);
        }

        /// <summary>Registers an entity delete for future execution without deleting data.</summary>
        /// <param name="entity">The entity to be deleted.</param>
        /// <exception cref="ArgumentNullException">No entity was provided.</exception>
        /// <exception cref="InvalidOperationException">The transaction has already been submitted.</exception>
        public void AddDelete(BlueprintEntity entity)
        {
            AddOperation(EOperationType.Delete, entity);
        }

        /// <summary>Registers a synchronous check that runs after all writes and before commit.</summary>
        /// <param name="validation">A check of the final state that throws if the transaction must be rolled back.</param>
        /// <remarks>Checks run in registration order within the same transaction on its reserved context. They may query, but must not write data, modify entities, or start asynchronous work.</remarks>
        /// <exception cref="ArgumentNullException">No validation was provided.</exception>
        /// <exception cref="ArgumentException">The validation is async void.</exception>
        /// <exception cref="InvalidOperationException">The transaction has already been submitted and the call does not originate from its preparation.</exception>
        public void AddValidation(Action validation)
        {
            ArgumentNullException.ThrowIfNull(validation);
            ValidateCallback(validation);
            lock (fSyncRoot)
            {
                EnsureCanAdd();
                fValidations.Add(validation);
            }
        }

        /// <summary>Rejects async void callbacks that would return before validation or preparation finishes.</summary>
        /// <param name="callback">The synchronous callback or invocation list.</param>
        /// <exception cref="ArgumentException">A callback uses an async state machine.</exception>
        private static void ValidateCallback(Delegate callback)
        {
            if (callback.GetInvocationList().Any(item => item.Method.IsDefined(typeof(AsyncStateMachineAttribute), false)))
            {
                throw new ArgumentException("Transaction callbacks must be synchronous; async void is not supported.", nameof(callback));
            }
        }

        /// <summary>Restricts additions after submission to the thread preparing this transaction.</summary>
        /// <exception cref="InvalidOperationException">The list has been reserved for execution.</exception>
        private void EnsureCanAdd()
        {
            if (fExecutionStarted && fPreparationThreadID != Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException("Operations and validations cannot be added after the transaction has been submitted.");
            }
        }

        /// <summary>Appends a valid entity reference to the shared list under synchronization.</summary>
        /// <param name="operationType">The operation type.</param>
        /// <param name="entity">The entity targeted by the operation.</param>
        /// <exception cref="ArgumentNullException">No entity was provided.</exception>
        /// <exception cref="InvalidOperationException">The transaction has already been submitted.</exception>
        private void AddOperation(EOperationType operationType, BlueprintEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            lock (fSyncRoot)
            {
                EnsureCanAdd();
                fOperations.Add(new Operation(operationType, entity));
            }
        }

        /// <summary>Atomically reserves the single execution and prevents further additions by the consumer.</summary>
        /// <exception cref="InvalidOperationException">The transaction has already been submitted.</exception>
        internal void BeginExecution()
        {
            lock (fSyncRoot)
            {
                if (fExecutionStarted)
                {
                    throw new InvalidOperationException("A transaction can be submitted only once.");
                }
                fExecutionStarted = true;
            }
        }

        /// <summary>Prepares operations within the active SQL transaction and freezes operations and final validations.</summary>
        /// <returns>Independent snapshots of the operations and validations.</returns>
        /// <exception cref="Exception">The preparation callback fails and rollback is required.</exception>
        internal (IReadOnlyList<Operation> Operations, IReadOnlyList<Action> Validations) PrepareExecution()
        {
            lock (fSyncRoot)
            {
                fPreparationThreadID = Environment.CurrentManagedThreadId;
            }
            try
            {
                fPrepare?.Invoke(this);
                lock (fSyncRoot)
                {
                    return (fOperations.ToArray(), fValidations.ToArray());
                }
            }
            finally
            {
                lock (fSyncRoot)
                {
                    fPreparationThreadID = 0;
                }
            }
        }

        /// <summary>Publishes the final outcome after the worker completes.</summary>
        /// <param name="isCommitted">Whether the database confirmed commit.</param>
        internal void CompleteExecution(bool isCommitted)
        {
            lock (fSyncRoot)
            {
                fIsCommitted = isCommitted;
                fIsCompleted = true;
            }
        }

        /// <summary>The operation types that can be registered in the transaction.</summary>
        public enum EOperationType
        {
            Insert,
            Update,
            Delete
        }

        /// <summary>Describes a registered operation with a fixed type and entity reference.</summary>
        public sealed class Operation
        {
            private readonly EOperationType fType;
            private readonly BlueprintEntity fEntity;

            /// <summary>Stores an operation description without executing it.</summary>
            /// <param name="operationType">The operation type.</param>
            /// <param name="entity">The entity reference validated by DatabaseTransaction.</param>
            internal Operation(EOperationType operationType, BlueprintEntity entity)
            {
                fType = operationType;
                fEntity = entity;
            }

            /// <summary>The registered operation type.</summary>
            public EOperationType Type
            {
                get
                {
                    return fType;
                }
            }

            /// <summary>The original entity for the operation; its values are not copied during registration.</summary>
            public BlueprintEntity Entity
            {
                get
                {
                    return fEntity;
                }
            }
        }
    }
}
