using Blueprint.BackgroundTasks.Schedule;

namespace Blueprint.BackgroundTasks
{
    /// <summary>Independently schedules and runs tasks without overlapping executions of the same task.</summary>
    public sealed class BackgroundTasksManager : IDisposable
    {
        private const int POLLING_INTERVAL_MILLISECONDS = 1000;
        private static readonly AsyncLocal<bool> fIsExecutingCallback = new();
        private readonly object fSyncRoot = new();
        private readonly Dictionary<string, TaskRegistration> fTasks = new(StringComparer.Ordinal);
        private readonly TimeProvider fTimeProvider;
        private readonly ITimer fTimer;
        private bool fIsStarted;
        private bool fIsStopping;
        private bool fIsStopped;
        private int fActiveChecks;

        /// <summary>Creates an independent manager. Call Start before registering or running tasks.</summary>
        /// <param name="timeProvider">An optional time source for isolated tests.</param>
        public BackgroundTasksManager(TimeProvider? timeProvider = null)
        {
            fTimeProvider = timeProvider ?? TimeProvider.System;
            fTimer = fTimeProvider.CreateTimer(CheckSchedules, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>Reports a task or scheduling failure with the corresponding task and exception.</summary>
        /// <remarks>Subscribers are called outside locks. Their exceptions are isolated so that other subscribers are still notified.</remarks>
        public event Action<BackgroundTask, Exception>? TaskFailed;

        /// <summary>A snapshot of registered tasks whose collection cannot be modified externally.</summary>
        public IReadOnlyList<BackgroundTask> Tasks
        {
            get
            {
                lock (fSyncRoot)
                {
                    return Array.AsReadOnly(fTasks.Values.Select(registration => registration.fTask).ToArray());
                }
            }
        }

        /// <summary>Registers a task and calculates its first execution from the registration time.</summary>
        /// <param name="task">The task, with a unique case-sensitive name.</param>
        /// <exception cref="ArgumentNullException">No task was provided.</exception>
        /// <exception cref="InvalidOperationException">The registration is duplicated, the schedule is invalid, or the manager is not accepting changes.</exception>
        /// <exception cref="ObjectDisposedException">The manager has been shut down.</exception>
        public void RegisterTask(BackgroundTask task)
        {
            ArgumentNullException.ThrowIfNull(task);
            lock (fSyncRoot)
            {
                ValidateAvailable();
                if (fTasks.ContainsKey(task.Name) || fTasks.Values.Any(registration => ReferenceEquals(registration.fTask, task)))
                {
                    throw new InvalidOperationException($"Background service '{task.Name}' or its instance is already registered.");
                }

                var now = fTimeProvider.GetUtcNow();
                var schedule = task.Schedule;
                var nextRunAt = CalculateNextRun(schedule, now, now);
                fTasks.Add(task.Name, new TaskRegistration(task, schedule, now, nextRunAt));
            }
        }

        /// <summary>Prevents new executions, waits for active work, and removes the registration.</summary>
        /// <param name="name">The task name.</param>
        /// <exception cref="ArgumentException">The name is empty.</exception>
        /// <exception cref="ArgumentNullException">No name was provided.</exception>
        /// <exception cref="KeyNotFoundException">No task exists with this name.</exception>
        /// <exception cref="InvalidOperationException">The call originates from a task or callback, or the manager is not accepting changes.</exception>
        /// <exception cref="ObjectDisposedException">The manager has been shut down.</exception>
        public void UnregisterTask(string name)
        {
            ValidateLifecycleChange();
            TaskRegistration registration;
            Task executionTask;
            lock (fSyncRoot)
            {
                ValidateAvailable();
                registration = FindTask(name);
                registration.fIsRemoving = true;
                registration.fNextRunAt = null;
                executionTask = registration.fExecutionTask;
            }

            executionTask.GetAwaiter().GetResult();
            lock (fSyncRoot)
            {
                if (fTasks.TryGetValue(name, out var current) && ReferenceEquals(current, registration))
                {
                    fTasks.Remove(name);
                }
            }
        }

        /// <summary>Returns the task with the specified name.</summary>
        /// <param name="name">The registered name.</param>
        /// <returns>The registered instance.</returns>
        /// <exception cref="ArgumentException">The name is empty.</exception>
        /// <exception cref="ArgumentNullException">No name was provided.</exception>
        /// <exception cref="KeyNotFoundException">No task exists with this name.</exception>
        /// <exception cref="ObjectDisposedException">The manager has been shut down.</exception>
        public BackgroundTask getTask(string name)
        {
            lock (fSyncRoot)
            {
                ObjectDisposedException.ThrowIf(fIsStopped, this);
                return FindTask(name).fTask;
            }
        }

        /// <summary>Requests immediate background execution without shifting the schedule.</summary>
        /// <param name="name">The task name.</param>
        /// <returns>True if the request was accepted, or false if the task is already running or being removed. This does not indicate successful completion.</returns>
        /// <exception cref="ArgumentException">The name is empty.</exception>
        /// <exception cref="ArgumentNullException">No name was provided.</exception>
        /// <exception cref="KeyNotFoundException">No task exists with this name.</exception>
        /// <exception cref="InvalidOperationException">The manager is not accepting executions.</exception>
        /// <exception cref="ObjectDisposedException">The manager has been shut down.</exception>
        public bool RunTaskNow(string name)
        {
            lock (fSyncRoot)
            {
                ValidateAvailable();
                return StartExecution(FindTask(name));
            }
        }

        /// <summary>Starts or resumes this manager's scheduling without requiring a database.</summary>
        /// <exception cref="ObjectDisposedException">The manager has been permanently shut down.</exception>
        /// <exception cref="InvalidOperationException">Tasks are still shutting down.</exception>
        public void Start()
        {
            lock (fSyncRoot)
            {
                ObjectDisposedException.ThrowIf(fIsStopped, this);
                if (fIsStopping)
                {
                    throw new InvalidOperationException("The background services manager is stopping.");
                }

                if (!fIsStarted)
                {
                    fIsStarted = true;
                    fTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(POLLING_INTERVAL_MILLISECONDS));
                }
            }
        }

        /// <summary>Stops scheduling and waits for running tasks, preserving registrations for a later Start.</summary>
        /// <exception cref="InvalidOperationException">The call originates from a task or callback.</exception>
        public void Pause()
        {
            StopScheduling(false);
        }

        /// <summary>Permanently stops scheduling, waits for running tasks, and releases this manager's resources.</summary>
        /// <remarks>Does not dispose a database. Repeated calls are safe.</remarks>
        /// <exception cref="InvalidOperationException">The call originates from a task or callback.</exception>
        public void Stop()
        {
            StopScheduling(true);
        }

        /// <summary>Waits for running tasks and permanently releases the manager, including when leaving a using block.</summary>
        /// <exception cref="InvalidOperationException">The call originates from a task or callback.</exception>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>Prevents a task or callback from waiting for work that depends on its own completion.</summary>
        /// <exception cref="InvalidOperationException">The operation could cause a deadlock.</exception>
        private static void ValidateLifecycleChange()
        {
            if (fIsExecutingCallback.Value)
            {
                throw new InvalidOperationException("Background work and its callbacks cannot stop or pause background task managers, or unregister tasks synchronously.");
            }
        }

        /// <summary>Detects schedule changes and due executions, isolating failures by task.</summary>
        /// <param name="state">The unused timer state.</param>
        private void CheckSchedules(object? state)
        {
            lock (fSyncRoot)
            {
                if (!fIsStarted)
                {
                    return;
                }

                fActiveChecks++;
            }

            var previousContext = fIsExecutingCallback.Value;
            fIsExecutingCallback.Value = true;
            try
            {
                var failures = new List<(BackgroundTask Task, Exception Exception)>();
                lock (fSyncRoot)
                {
                    if (!fIsStarted)
                    {
                        return;
                    }

                    var now = fTimeProvider.GetUtcNow();
                    foreach (var registration in fTasks.Values.ToArray())
                    {
                        if (registration.fIsRemoving)
                        {
                            continue;
                        }

                        try
                        {
                            var schedule = registration.fTask.Schedule;
                            if (!ReferenceEquals(schedule, registration.fSchedule))
                            {
                                registration.fSchedule = schedule;
                                registration.fActivatedAt = now;
                                registration.fNeedsReschedule = true;
                            }

                            if (registration.fNeedsReschedule)
                            {
                                registration.fNeedsReschedule = false;
                                registration.fNextRunAt = CalculateNextRun(schedule, now, registration.fActivatedAt);
                            }

                            if (registration.fNextRunAt.HasValue && registration.fNextRunAt.Value <= now)
                            {
                                registration.fNextRunAt = CalculateNextRun(schedule, now, registration.fActivatedAt);
                                StartExecution(registration);
                            }
                        }
                        catch (Exception exception)
                        {
                            registration.fNextRunAt = null;
                            failures.Add((registration.fTask, exception));
                        }
                    }
                }

                foreach (var failure in failures)
                {
                    NotifyFailure(failure.Task, failure.Exception);
                }
            }
            finally
            {
                fIsExecutingCallback.Value = previousContext;
                lock (fSyncRoot)
                {
                    fActiveChecks--;
                    Monitor.PulseAll(fSyncRoot);
                }
            }
        }

        /// <summary>Atomically reserves a task while holding the manager lock.</summary>
        /// <param name="registration">The task registration.</param>
        /// <returns>True if the execution was accepted.</returns>
        private bool StartExecution(TaskRegistration registration)
        {
            if (registration.fIsRemoving || !registration.fExecutionTask.IsCompleted)
            {
                return false;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            registration.fExecutionTask = completion.Task;
            registration.fTask.Status = EBackgroundTaskStatus.Running;
            try
            {
                _ = Task.Run(() => ExecuteTask(registration.fTask, completion));
                return true;
            }
            catch
            {
                registration.fTask.Status = EBackgroundTaskStatus.Idle;
                completion.SetResult();
                throw;
            }
        }

        /// <summary>Runs the task and failure notification outside locks, always resetting its status.</summary>
        /// <param name="task">The task to execute.</param>
        /// <param name="completion">The completion signal awaited by removal and shutdown.</param>
        private void ExecuteTask(BackgroundTask task, TaskCompletionSource completion)
        {
            var previousContext = fIsExecutingCallback.Value;
            fIsExecutingCallback.Value = true;
            try
            {
                task.Run();
            }
            catch (Exception exception)
            {
                NotifyFailure(task, exception);
            }
            finally
            {
                fIsExecutingCallback.Value = previousContext;
                lock (fSyncRoot)
                {
                    task.Status = EBackgroundTaskStatus.Idle;
                    completion.SetResult();
                }
            }
        }

        /// <summary>Notifies each subscriber independently so that one handler's failure does not affect other tasks.</summary>
        /// <param name="task">The task that failed.</param>
        /// <param name="exception">The execution or scheduling error.</param>
        private void NotifyFailure(BackgroundTask task, Exception exception)
        {
            var handlers = TaskFailed;
            if (handlers == null)
            {
                return;
            }

            foreach (Action<BackgroundTask, Exception> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(task, exception);
                }
                catch (Exception)
                {
                    // A subscriber must not interrupt scheduling or other notifications.
                }
            }
        }

        /// <summary>Stops new executions and waits for callbacks and tasks without holding a lock while waiting.</summary>
        /// <param name="permanently">Whether to dispose the timer and release registrations.</param>
        /// <exception cref="InvalidOperationException">The call originates from a task or callback.</exception>
        private void StopScheduling(bool permanently)
        {
            ValidateLifecycleChange();
            Task[] executions;
            lock (fSyncRoot)
            {
                while (fIsStopping)
                {
                    Monitor.Wait(fSyncRoot);
                }

                if (fIsStopped)
                {
                    return;
                }

                fIsStopping = true;
                fIsStarted = false;
                fTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                while (fActiveChecks != 0)
                {
                    Monitor.Wait(fSyncRoot);
                }

                executions = fTasks.Values.Select(registration => registration.fExecutionTask).ToArray();
                foreach (var registration in fTasks.Values)
                {
                    registration.fNeedsReschedule = true;
                }
            }

            try
            {
                Task.WhenAll(executions).GetAwaiter().GetResult();
            }
            finally
            {
                lock (fSyncRoot)
                {
                    if (permanently)
                    {
                        fIsStopped = true;
                        fTimer.Dispose();
                        fTasks.Clear();
                        TaskFailed = null;
                    }

                    fIsStopping = false;
                    Monitor.PulseAll(fSyncRoot);
                }
            }
        }

        /// <summary>Finds a registration while holding the manager lock.</summary>
        /// <param name="name">The registered name.</param>
        /// <returns>The registration with the specified name.</returns>
        /// <exception cref="ArgumentException">The name is empty.</exception>
        /// <exception cref="ArgumentNullException">No name was provided.</exception>
        /// <exception cref="KeyNotFoundException">The name is not registered.</exception>
        private TaskRegistration FindTask(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return fTasks.TryGetValue(name, out var registration) ? registration : throw new KeyNotFoundException($"Background service '{name}' is not registered.");
        }

        /// <summary>Verifies that the manager accepts registrations and new work.</summary>
        /// <exception cref="ObjectDisposedException">The manager has been shut down.</exception>
        /// <exception cref="InvalidOperationException">The manager is inactive or waiting for tasks.</exception>
        private void ValidateAvailable()
        {
            ObjectDisposedException.ThrowIf(fIsStopped, this);
            if (!fIsStarted || fIsStopping)
            {
                throw new InvalidOperationException("The background services manager is not accepting new work.");
            }
        }

        /// <summary>Calculates and validates an execution time strictly in the future.</summary>
        /// <param name="schedule">The active schedule.</param>
        /// <param name="after">The current time.</param>
        /// <param name="activatedAt">The interval's starting point.</param>
        /// <returns>The next execution time in UTC, or null.</returns>
        /// <exception cref="InvalidOperationException">The schedule returns a time that is not in the future.</exception>
        private static DateTimeOffset? CalculateNextRun(BackgroundTaskSchedule schedule, DateTimeOffset after, DateTimeOffset activatedAt)
        {
            var previousContext = fIsExecutingCallback.Value;
            fIsExecutingCallback.Value = true;
            try
            {
                var nextRunAt = schedule.CalculateNextRunTime(after, activatedAt);
                if (nextRunAt.HasValue && nextRunAt.Value <= after)
                {
                    throw new InvalidOperationException("A background service schedule must return a future execution time.");
                }

                return nextRunAt?.ToUniversalTime();
            }
            finally
            {
                fIsExecutingCallback.Value = previousContext;
            }
        }

        /// <summary>Keeps scheduling state and active work exclusively within the manager.</summary>
        private sealed class TaskRegistration
        {
            public readonly BackgroundTask fTask;
            public BackgroundTaskSchedule fSchedule;
            public DateTimeOffset fActivatedAt;
            public DateTimeOffset? fNextRunAt;
            public Task fExecutionTask = Task.CompletedTask;
            public bool fNeedsReschedule;
            public bool fIsRemoving;

            /// <summary>Stores the task and its already calculated first execution.</summary>
            /// <param name="task">The registered task.</param>
            /// <param name="schedule">The registration's schedule.</param>
            /// <param name="activatedAt">The schedule's starting point.</param>
            /// <param name="nextRunAt">The first execution time in UTC, or null.</param>
            public TaskRegistration(BackgroundTask task, BackgroundTaskSchedule schedule, DateTimeOffset activatedAt, DateTimeOffset? nextRunAt)
            {
                fTask = task;
                fSchedule = schedule;
                fActivatedAt = activatedAt;
                fNextRunAt = nextRunAt;
            }
        }
    }
}
