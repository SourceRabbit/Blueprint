# BlueprintLog

`BlueprintLog` is the **abstract base class** for application logging in the `Blueprint.Log` namespace. It defines the shared API for writing, reading, counting, paginating, and deleting events. It cannot be instantiated directly; create a concrete implementation and use it through a `BlueprintLog` reference.

The base class captures `Environment.MachineName` once during construction and exposes it through the read-only `MachineName` property. `WriteToLog`, `ReadFromLog`, `CountFromLog`, and `DeleteFromLog` are abstract methods: the concrete implementation supplies persistence, queries, deletion, and argument validation according to their contract. The base class does not open database connections or create storage.

## Implementations

| Implementation | Status | Documentation |
| --- | --- | --- |
| `SqlLog` | Available; implements logging in SQL Server. | [SqlLog](Log-SqlLog.md): initialization, examples, SQL table, permissions, and execution behavior. |
| `MySqlLog` | Coming Soon; not implemented yet. | [MySqlLog (Coming Soon)](Log-MySqlLog.md): reserved for MySQL logging configuration and usage. |

Application code can depend on `BlueprintLog` while choosing the concrete implementation during initialization. For a working example, see [Create a SQL log](Log-SqlLog.md#create-a-sql-log). Connection settings, table creation, timestamp generation and precision, and transaction behavior belong to the implementation's documentation.

## Properties

| Property | Purpose |
| --- | --- |
| [MachineName](#machinename) | Gets the originating machine name captured when this log instance was created. |

### MachineName

**Type:** `string`. **Access:** read-only.

The value of `Environment.MachineName` captured once by the base constructor. Each event written through this instance records that machine name. It has no setter and is not supplied as an argument to `WriteToLog`.

Read it through `log.MachineName`, for example when requesting events from the current machine. Reading the property does not access stored events. Reads and counts include all machines unless the caller explicitly supplies a machine-name filter.

## Methods

| Method | Purpose |
| --- | --- |
| [WriteToLog(level, message, logger, exception)](#writetolog) | Persists one event with its level, message, optional source, and optional exception. |
| [ReadFromLog(...)](#readfromlog) | Reads events by UTC interval, optionally filtering by level and machine, with unpaged or paged results. |
| [CountFromLog(...)](#countfromlog) | Counts matching events before pagination using the same interval, level, and machine filters. |
| [DeleteFromLog(dateTimeUtcBefore)](#deletefromlog) | Deletes all events strictly before a UTC cutoff, across all levels and machines. |

All four methods are abstract; the selected log provider implements persistence, queries, and deletion. The examples below assume an initialized `BlueprintLog` variable named `log`. See [Create a SQL log](Log-SqlLog.md#create-a-sql-log) for initialization. Examples are independent and use these imports as needed:

```csharp
using Blueprint.Log;
using System;
using System.Collections.Generic;
```

### WriteToLog

The shared method is `WriteToLog(EBlueprintLogLevel level, string message, string? logger = null, Exception? exception = null)` and returns `void`.

| Parameter | Type | Meaning |
| --- | --- | --- |
| `level` | `EBlueprintLogLevel` | A defined log level chosen by the caller. |
| `message` | `string` | The final message; it must not be null, empty, or whitespace-only. |
| `logger` | `string?` | An optional source label of up to 500 characters; defaults to `null`. |
| `exception` | `Exception?` | An optional exception associated with the event; defaults to `null`. |

The contract requires the event to be persisted before the call returns, with an automatically assigned UTC timestamp and the originating machine name. Callers do not supply a timestamp or machine name to this method. Persistence failures propagate to the caller.

Write a normal event or add a source label:

```csharp
log.WriteToLog(EBlueprintLogLevel.Info, "Application started.");
log.WriteToLog(EBlueprintLogLevel.Warn, "Product 320 has no image.", "ProductImport");
```

To record a failure, pass the exception along with a message describing the failed operation:

```csharp
try
{
    string contents = System.IO.File.ReadAllText("products.csv");
    log.WriteToLog(EBlueprintLogLevel.Info, $"Read {contents.Length} characters from products.csv.", "ProductImport");
}
catch (System.IO.IOException exception)
{
    log.WriteToLog(EBlueprintLogLevel.Error, "Could not read products.csv.", "ProductImport", exception);
}
```

See [SqlLog: Write an event](Log-SqlLog.md#write-an-event) for SQL Server timestamp behavior and exception storage.

### ReadFromLog

`ReadFromLog` returns `IReadOnlyList<LogEntry>` in descending insertion `Id` order. Its overloads accept the following arguments:

| Parameter | Type | Meaning |
| --- | --- | --- |
| `dateTimeUtcFrom` | `DateTime` | The inclusive start of the UTC interval. |
| `dateTimeUtcTo` | `DateTime` | The exclusive end of the UTC interval. |
| `level` | `EBlueprintLogLevel` | One exact level, when using an overload with a level filter. |
| `machineName` | `string` | The originating machine, when using an overload with both level and machine filters. |
| `skip` | `int` | The nonnegative number of matching entries to skip in a paged overload. |
| `take` | `int` | The positive maximum number of entries to return in a paged overload. |

The three unpaged overloads read all matching events:

| Overload | Filters |
| --- | --- |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo)` | The UTC interval, across all levels and machines. |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, level)` | The UTC interval and exactly one level. |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, level, machineName)` | The UTC interval, exactly one level, and the supplied machine name. |

Both boundaries are `DateTime` values with `Kind = DateTimeKind.Utc`. The start is inclusive and the end is exclusive: `dateTimeUtcFrom <= Logged < dateTimeUtcTo`. A reversed interval or a non-UTC boundary is invalid. Equal boundaries return an empty list.

The level filter selects one exact `EBlueprintLogLevel`, not a minimum severity. When supplied, `machineName` must be nonempty, not whitespace-only, and no longer than 255 characters. Reads are not automatically restricted to the current instance's `MachineName`.

Results are read-only snapshots containing immutable [LogEntry](#logentry) objects. No matches produce an empty list, never `null`. Unpaged reads have no implicit row limit. Reading failures propagate rather than being returned as empty results.

For example, read the current UTC day with each filter combination:

```csharp
DateTime fromUtc = DateTime.UtcNow.Date;
DateTime toUtc = fromUtc.AddDays(1);

IReadOnlyList<LogEntry> events = log.ReadFromLog(fromUtc, toUtc);
IReadOnlyList<LogEntry> errors = log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error);
IReadOnlyList<LogEntry> machineErrors = log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, log.MachineName);

Console.WriteLine($"Events: {events.Count}; errors: {errors.Count}; errors from this machine: {machineErrors.Count}");
foreach (LogEntry entry in machineErrors)
{
    Console.WriteLine($"{entry.Logged:O} [{entry.Level}] {entry.Message}");
}
```

See [SqlLog: Read events](Log-SqlLog.md#read-events) for SQL Server filtering details.

#### Paged reads

To read a page, append `skip` and `take` to the corresponding read call:

| Overload | Result |
| --- | --- |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, skip, take)` | A page across all levels and machines. |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, level, skip, take)` | A page for exactly one level. |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, level, machineName, skip, take)` | A page for exactly one level and machine. |

`skip` is a nonnegative `int`; `take` is a positive `int`. Paged reads return `IReadOnlyList<LogEntry>` in descending `Id` order after applying the filters. A page beyond the matching events is empty, and the last page may contain fewer than `take` entries.

For example, request the first 25 matching entries with each filter combination:

```csharp
DateTime fromUtc = DateTime.UtcNow.Date;
DateTime toUtc = fromUtc.AddDays(1);
int skip = 0;
int take = 25;

IReadOnlyList<LogEntry> page = log.ReadFromLog(fromUtc, toUtc, skip, take);
IReadOnlyList<LogEntry> errorPage = log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, skip, take);
IReadOnlyList<LogEntry> machineErrorPage = log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, log.MachineName, skip, take);
Console.WriteLine($"Page entries: {page.Count}; errors: {errorPage.Count}; errors from this machine: {machineErrorPage.Count}");
```

Use [CountFromLog](#countfromlog) with the same filters to obtain the total number of matching events before pagination.

### CountFromLog

`CountFromLog` returns a `long` with the total number of matching events before pagination. It supports the same filter combinations as reads:

| Overload | Filters |
| --- | --- |
| `CountFromLog(dateTimeUtcFrom, dateTimeUtcTo)` | The UTC interval, across all levels and machines. |
| `CountFromLog(dateTimeUtcFrom, dateTimeUtcTo, level)` | The UTC interval and exactly one level. |
| `CountFromLog(dateTimeUtcFrom, dateTimeUtcTo, level, machineName)` | The UTC interval, exactly one level, and the supplied machine name. |

The parameters have the same types and validation rules as [ReadFromLog](#readfromlog): UTC `DateTime` boundaries, an optional exact `EBlueprintLogLevel` filter, and a machine-name `string` when using the last overload. No matches or equal boundaries produce zero; counting failures propagate. There are no `skip` or `take` arguments because counting always covers the full matching set.

For example, count events for the current UTC day:

```csharp
DateTime fromUtc = DateTime.UtcNow.Date;
DateTime toUtc = fromUtc.AddDays(1);

long totalCount = log.CountFromLog(fromUtc, toUtc);
long errorCount = log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error);
long machineErrorCount = log.CountFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, log.MachineName);
Console.WriteLine($"Events: {totalCount}; errors: {errorCount}; errors from this machine: {machineErrorCount}");
```

See [SqlLog: Count and paginate events](Log-SqlLog.md#count-and-paginate-events) for a complete grid example and the effects of concurrent writes between count and read calls.

### DeleteFromLog

`DeleteFromLog(DateTime dateTimeUtcBefore)` returns `void` and deletes every event whose stored timestamp satisfies `Logged < dateTimeUtcBefore`. Events exactly at the cutoff or later are retained. Deletion covers all levels and machines in this log, including events written by other instances.

The argument must have `Kind = DateTimeKind.Utc`; local and unspecified values throw `ArgumentException` without deleting anything. Fractional seconds in the cutoff are preserved. No matching events, including an empty log, is a successful no-op. Storage failures propagate to the caller, and previously returned `LogEntry` snapshots remain unchanged.

For example, delete events older than thirty days:

```csharp
DateTime cutoffUtc = DateTime.UtcNow.AddDays(-30);
log.DeleteFromLog(cutoffUtc);
```

Call the method explicitly when cleanup is required; it does not enable a recurring retention policy. Custom `BlueprintLog` implementations must implement this abstract method. See [SqlLog: Delete old events](Log-SqlLog.md#delete-old-events) for SQL permissions and transaction behavior.

## Log levels

`EBlueprintLogLevel` belongs to the `Blueprint.Log` namespace and defines these values:

| Value | Typical use |
| --- | --- |
| `Trace` | Detailed execution diagnostics. |
| `Debug` | Information used to diagnose problems. |
| `Info` | Normal application events. |
| `Warn` | A problem that allows execution to continue. |
| `Error` | A failed operation. |
| `Fatal` | A critical failure that prevents the application from continuing. |

The caller chooses the level when writing an event. The shared API has no minimum-level configuration; its read and count filters select one exact level. Undefined enum values are invalid.

## LogEntry

`LogEntry` is the immutable result type in the `Blueprint.Log` namespace. It exposes these read-only properties:

| Property | Type | Meaning |
| --- | --- | --- |
| `Id` | `long` | The database-generated insertion ID. |
| `Logged` | `DateTime` | The stored UTC timestamp, returned with `Kind = DateTimeKind.Utc`. |
| `Level` | `EBlueprintLogLevel` | The stored event level. |
| `Message` | `string` | The stored message. |
| `Logger` | `string?` | The source label, or `null`. |
| `Exception` | `string?` | The stored exception text, or `null`. |
| `MachineName` | `string` | The machine name recorded by the writer. |

Reading does not reconstruct the original `Exception` object. Later writes do not modify an already returned entry or result collection. See [SqlLog: SQL table](Log-SqlLog.md#sql-table) for the SQL Server column types and timestamp precision.

## Related topics

[SqlLog](Log-SqlLog.md) · [Getting Started](GettingStarted.md) · [Contributing](Contributing.md)
