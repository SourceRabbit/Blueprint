# Background Tasks

Blueprint can run named background tasks on interval or daily schedules. `BackgroundTasksManager` is independent of the database: create it directly, call `Start()`, and call `Stop()` or `Dispose()` when the application finishes. Tasks that use the Entity API still require an active database with their contracts registered.

## Define a task

Create a subclass of `BackgroundTask` and implement the synchronous `Run()` method. This example uses the [Product contract](Database-EShopCheckoutTutorial.md#define-a-contract) from the EShop checkout tutorial:

```csharp
using Blueprint.BackgroundTasks;
using Blueprint.BackgroundTasks.Schedule;
using Blueprint.Entities;
using System;
using System.Linq;

namespace BlueprintManual
{
    /// <summary>Periodically reports the number of products in the catalog.</summary>
    public sealed class ReportProducts : BackgroundTask
    {
        /// <summary>Configures a catalog report every five minutes.</summary>
        public ReportProducts() : base("Catalog report", "Reports the number of stored products.", new IntervalSchedule(TimeSpan.FromMinutes(5)))
        {
        }

        /// <summary>Reads the current product count and writes it to the console.</summary>
        /// <exception cref="Exception">Reading the catalog or writing the report fails.</exception>
        public override void Run()
        {
            int count = Product.Where(item => item.ID > 0).Count();
            Console.WriteLine($"Products: {count}");
        }
    }
}
```

Register entities before running a task that accesses them. Keep the manager inside the database lifetime so that its tasks finish before the database is disposed:

```csharp
using (BlueprintDatabase database = new SqlDatabase("MyEshopDatabase", sqlServerConnectionString, languages))
{
    database.RegisterEntity<Product>();
    using (BackgroundTasksManager manager = new BackgroundTasksManager())
    {
        manager.TaskFailed += (failedTask, exception) =>
        {
            Console.Error.WriteLine($"{failedTask.Name}: {exception.Message}");
        };
        manager.Start();
        ReportProducts task = new ReportProducts();
        manager.RegisterTask(task);
        Console.ReadLine();
    }
}
```

Import `Blueprint.Database` and `Blueprint.Database.Sql` for this lifetime example, and supply the connection settings and languages as shown in [SQL Server](Database-SqlDatabase.md). The control examples below run inside the manager's using block. A manager whose tasks do not access entities needs no database.

Names must be nonempty and unique within the manager; comparison is case-sensitive. A task instance cannot be registered twice in that manager. Supply a non-null description and schedule.

## Choose a schedule

| Schedule | Behavior |
| --- | --- |
| `IntervalSchedule(TimeSpan interval)` | Runs on a cadence from registration, with the first run after the interval. The interval must be positive. Missed intervals are skipped instead of creating a backlog. |
| `DailySchedule(IEnumerable<TimeOnly> times, TimeZoneInfo timeZone)` | Runs at the supplied local times in an explicit time zone. Times must be nonempty; duplicates are removed. |

You can replace the task's `Schedule` after registration. For example, use two daily UTC times:

```csharp
using Blueprint.BackgroundTasks.Schedule;

task.Schedule = new DailySchedule(new TimeOnly[] { new TimeOnly(9, 0), new TimeOnly(17, 30) }, TimeZoneInfo.Utc);
```

For a local time zone, pass the appropriate `TimeZoneInfo` for your deployment. At daylight-saving transitions, a nonexistent local time is skipped. An ambiguous time runs only at its first occurrence.

Scheduling is checked periodically, so execution is not an exact-time or real-time guarantee. Scheduled work is in-process and requires the application to be running; registrations are not a durable job queue.

For a custom schedule, extend `BackgroundTaskSchedule` and override `CalculateNextRunTime(DateTimeOffset after, DateTimeOffset activatedAt)`. Return a time strictly after `after`, or null if there is no next execution. An invalid next time is rejected.

## Inspect and control tasks

```csharp
using Blueprint.BackgroundTasks;

BackgroundTask registeredTask = manager.getTask("Catalog report");
bool accepted = manager.RunTaskNow(registeredTask.Name);
Console.WriteLine($"Accepted: {accepted}; status: {registeredTask.Status}");
```

- `Start()` starts or resumes scheduling; repeated calls while running are safe. Call it before registering tasks.
- `Pause()` waits for running work and preserves registrations for a later `Start()`.
- `Stop()` and `Dispose()` wait for work, release registrations and the timer, and permanently stop the manager. Repeated calls are safe; a stopped manager cannot restart.
- `Tasks` returns a read-only snapshot of registered tasks.
- `getTask(name)` returns the registered instance; an unknown name throws.
- `RunTaskNow(name)` requests a background execution without moving its regular schedule. True means the request was accepted, not that the task completed successfully. False means it is already running or being removed.
- `Status` is `Idle` or `Running`. After success or failure it returns to `Idle`; use `TaskFailed` to observe failures.
- `UnregisterTask(name)` prevents new executions, waits for an active execution, and removes the task.

For example, from application shutdown or configuration code outside a task:

```csharp
manager.UnregisterTask("Catalog report");
```

## Errors, concurrency, and shutdown

The manager prevents overlapping executions of the same task. Different tasks can run concurrently, although database operations on the same database instance are serialized.

`TaskFailed` reports execution and scheduling exceptions. Subscribers run outside manager locks, and a failing subscriber does not prevent the other subscribers from being notified. Record errors in your application's logging system. A failed run does not permanently disable the registered task; later scheduled executions can still run.

Keep work inside the synchronous `Run()` call. Do not implement `async void Run()` or start unobserved asynchronous work: the manager cannot wait for work that outlives the method.

`manager.Stop()` or `manager.Dispose()` stops new scheduling and waits for running tasks and callbacks. It does not dispose the database. Stop all managers that use the database before calling `database.Dispose()`. There is no automatic cancellation token for `Run()`, so long-running work delays shutdown. Calling `Stop()`, `Pause()`, `Dispose()`, or synchronously unregistering a task from a task or its callbacks is rejected to prevent self-deadlocks.

## Related topics

[Database lifetime](Database-SqlDatabase.md#application-lifetime) · [Queries and CRUD](Entitites-BlueprintEntity.md#queries-and-crud) · [Troubleshooting](Troubleshooting.md)
