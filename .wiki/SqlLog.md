# SqlLog

`SqlLog` is the SQL Server implementation of the abstract `BlueprintLog` class. Both classes belong to the `Blueprint.Log` namespace. See [BlueprintLog](BlueprintLog.md) for the shared API, log levels, and `LogEntry` result type.

This page covers SQL Server initialization, writing and reading events, counting and pagination, the SQL table, and execution behavior. These storage and connection details apply to `SqlLog`; see [MySqlLog (Coming Soon)](MySqlLog.md) for the planned MySQL implementation.

## Create a SQL log

Add a Blueprint reference and its dependencies as described in [Getting Started](Getting-Started.md). `SqlLog` is independent of `BlueprintDatabase`: it does not require an active entity database, entity registration, or NLog configuration.

This complete console example creates the log database and table if they are missing, then writes two events:

```csharp
using Blueprint.Log;
using System;

namespace BlueprintManual
{
    /// <summary>Demonstrates initialization and synchronous SQL logging.</summary>
    internal static class Program
    {
        /// <summary>Initializes a SQL log and records application events.</summary>
        /// <exception cref="Exception">Log initialization or persistence fails.</exception>
        private static void Main()
        {
            string connectionString = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
            BlueprintLog log = new SqlLog(connectionString, "BlueprintLogs", "Log");

            log.WriteToLog(EBlueprintLogLevel.Info, "Application started.");
            log.WriteToLog(EBlueprintLogLevel.Warn, "Product 320 has no image.", "ProductImport");

            Console.WriteLine($"Logging from {log.MachineName}");
        }
    }
}
```

The connection string is a local development example. Supply the server, authentication, and certificate settings appropriate to your deployment. Running this example creates or reuses `BlueprintLogs.dbo.Log` and inserts two rows.

## Constructor and initialization

Create `SqlLog` with these three arguments, in order:

| Parameter | Type | Meaning |
| --- | --- | --- |
| `sqlConnectionString` | `string` | SQL Server connection settings. A server is required; `AttachDBFilename` is not supported. |
| `databaseName` | `string` | The database to use. Overrides `Database` or `Initial Catalog` in the connection string. |
| `tableName` | `string` | The unqualified table name in the `dbo` schema, such as `Log`. Supply the name without SQL quoting. |

Database and table names must contain 1 to 128 characters, with no control characters or surrounding whitespace. System databases (`master`, `model`, `msdb`, and `tempdb`) are rejected as log destinations.

Initialization captures `Environment.MachineName` once, creates the database if missing, and creates the table if missing. The captured name is available through the read-only `MachineName` property and is included in every row written by that instance. It is not a `WriteToLog` argument.

The SQL account must be able to connect to `master` and the target database, insert and select log rows, and create the database or table when needed. Initialization failures propagate to the caller.

The timestamp default is defined only when creating a new table. Existing tables are reused without schema migration and must already support the columns and default shown below.

## Write an event

`WriteToLog` returns `void` and takes these arguments in order:

| Parameter | Type | Required | Meaning |
| --- | --- | --- | --- |
| `level` | `EBlueprintLogLevel` | Yes | A defined enum value. |
| `message` | `string` | Yes | The final message. Null, empty, or whitespace-only messages are rejected. |
| `logger` | `string?` | No | A source label of up to 500 characters. Defaults to `null`. |
| `exception` | `Exception?` | No | The exception associated with the event. Defaults to `null`. |

SQL Server assigns `Logged` through the column's `DEFAULT (SYSUTCDATETIME())`, rounded to whole seconds by the `datetime2(0)` column. `WriteToLog` omits `Logged` from its `INSERT`. The timestamp comes from the database server's UTC clock and represents the time of insertion. Callers do not supply a timestamp; remove the former `DateTime` argument from existing `WriteToLog` calls and rebuild consumers.

`SqlLog` stores the final message as supplied; it does not expand message templates. The `logger` value is also supplied by the caller and is not inferred from the calling class or method.

To record an exception, pass the exception object along with a message explaining the operation that failed. With the `log` instance from the previous example:

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

The exception is stored using `Exception.ToString()`, including its type, message, inner exceptions, and stack trace when available. Omitted `logger` and `exception` values become SQL `NULL`. An explicitly supplied empty logger string remains empty.

## Read events

The three unpaged `ReadFromLog` overloads return `IReadOnlyList<LogEntry>` containing all matching rows in descending `Id` order:

| Overload | Filters |
| --- | --- |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo)` | The UTC interval, across all levels and machines. |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, level)` | The UTC interval and exactly one `EBlueprintLogLevel`. |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, level, machineName)` | The UTC interval, exactly one level, and the supplied machine name. |

Both date arguments are `DateTime` values with `Kind` equal to `DateTimeKind.Utc`. The start is inclusive and the end is exclusive: `dateTimeUtcFrom <= Logged < dateTimeUtcTo`. Equal boundaries return an empty list; a reversed interval or a non-UTC boundary throws an `ArgumentException`. Fractional boundaries retain their precision when filtering stored timestamps.

The level filter matches only the selected level, not a severity threshold. The machine filter uses SQL equality under the column's database collation. Its string must be nonempty, not whitespace-only, and no longer than 255 characters. The first two overloads include other machines and previous application runs; they do not restrict reads to the current instance's `MachineName`.

With the `log` instance from the initialization example, read the current UTC day and inspect errors from this machine:

```csharp
using System.Collections.Generic;

DateTime fromUtc = DateTime.UtcNow.Date;
DateTime toUtc = fromUtc.AddDays(1);

IReadOnlyList<LogEntry> events = log.ReadFromLog(fromUtc, toUtc);
IReadOnlyList<LogEntry> errors = log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error);
IReadOnlyList<LogEntry> machineErrors = log.ReadFromLog(fromUtc, toUtc, EBlueprintLogLevel.Error, log.MachineName);

foreach (LogEntry entry in machineErrors)
{
    Console.WriteLine($"{entry.Logged:O} [{entry.Level}] {entry.MachineName}: {entry.Message}");
    if (entry.Exception != null)
    {
        Console.WriteLine(entry.Exception);
    }
}
```

Results are fully materialized before the connection is returned to the pool. An empty result is an empty list, never `null`. These unpaged overloads have no implicit row limit; use the paged overloads below when displaying results in a grid. Ordering follows insertion IDs, including when multiple events share the same whole-second timestamp or existing rows have timestamps out of insertion order.

Each result is an immutable [LogEntry](BlueprintLog.md#logentry). The exception property contains stored text, and later database writes do not change an already returned snapshot. An unrecognized stored level throws `InvalidOperationException`; SQL failures propagate instead of being reported as an empty result.

## Count and paginate events

`CountFromLog` returns a `long` with the total number of matching rows before pagination. It supports the same three filter combinations as reads:

| Overload | Filters |
| --- | --- |
| `CountFromLog(dateTimeUtcFrom, dateTimeUtcTo)` | The UTC interval, across all levels and machines. |
| `CountFromLog(dateTimeUtcFrom, dateTimeUtcTo, level)` | The UTC interval and exactly one level. |
| `CountFromLog(dateTimeUtcFrom, dateTimeUtcTo, level, machineName)` | The UTC interval, exactly one level, and the supplied machine name. |

Counting uses SQL `COUNT_BIG(*)` without loading event contents. An empty result or equal-bound interval returns zero. UTC, interval order, level, and machine-name validation are identical to reads. SQL failures propagate; they are not reported as a zero count.

For a page of results, append `skip` and `take` to the corresponding read call:

| Overload | Result |
| --- | --- |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, skip, take)` | A page across all levels and machines. |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, level, skip, take)` | A page for exactly one level. |
| `ReadFromLog(dateTimeUtcFrom, dateTimeUtcTo, level, machineName, skip, take)` | A page for exactly one level and machine. |

`skip` is an `int` greater than or equal to zero: the number of matching rows to skip. `take` is an `int` greater than zero: the maximum number of rows to return. Invalid values throw `ArgumentOutOfRangeException`. A page beyond the matching rows is an empty list; the final page may contain fewer than `take` rows.

SQL applies the filters first, orders by `Id DESC`, and uses parameterized `OFFSET` / `FETCH` to return only the requested page. All paged overloads return `IReadOnlyList<LogEntry>` with the same field mapping and read-only behavior as unpaged reads. The existing unpaged overloads remain available.

For a CMS grid, use the same interval, level, and machine for both calls. This example requests the first page of errors from the current machine, with 25 rows per page:

```csharp
DateTime fromUtc = DateTime.UtcNow.Date;
DateTime toUtc = fromUtc.AddDays(1);
EBlueprintLogLevel level = EBlueprintLogLevel.Error;
string machineName = log.MachineName;
int pageNumber = 1;
int pageSize = 25;
int skip = checked((pageNumber - 1) * pageSize);

long totalCount = log.CountFromLog(fromUtc, toUtc, level, machineName);
long totalPages = totalCount == 0 ? 1 : (totalCount - 1) / pageSize + 1;
IReadOnlyList<LogEntry> page = log.ReadFromLog(fromUtc, toUtc, level, machineName, skip, pageSize);

Console.WriteLine($"Page {pageNumber} of {totalPages}: {page.Count} rows, {totalCount} total.");
foreach (LogEntry entry in page)
{
    Console.WriteLine($"{entry.Logged:O} [{entry.Level}] {entry.Message}");
}
```

The UI should validate its one-based page number and positive page size, reset to the first page when filters change, and clamp a page that is beyond the current total. The example treats an empty result as one empty display page. Counts are 64-bit; keep that type when calculating totals, and handle the `int` offset limit when converting UI page numbers.

Counting and reading are separate SQL operations, not one database snapshot. Concurrent inserts or deletions can change the total and shift rows between page requests, causing repeats or omissions while navigating a live log. Keep the same interval and filters during navigation and refresh the count and first page when updating the view. An already returned page remains unchanged.

## Log levels

The caller chooses an [EBlueprintLogLevel](BlueprintLog.md#log-levels). `SqlLog` stores its name, such as `Error`, rather than its numeric enum value. All defined levels are written; there is no minimum-level filter. Casting an undefined numeric value to the enum results in an `ArgumentOutOfRangeException` when writing or filtering reads and counts.

## SQL table

For `tableName` equal to `Log`, the constructor creates `dbo.Log` with this schema:

| Column | SQL Server type | Allows NULL | Stored value |
| --- | --- | --- | --- |
| `Id` | `bigint IDENTITY(1,1)` | No | Database-generated primary key. |
| `Logged` | `datetime2(0)` | No | The SQL Server UTC time at insertion, generated by `DEFAULT (SYSUTCDATETIME())` and rounded to whole seconds. |
| `Level` | `varchar(10)` | No | The enum value's name. |
| `Message` | `nvarchar(max)` | No | The supplied message. |
| `Logger` | `nvarchar(500)` | Yes | The optional source label. |
| `Exception` | `nvarchar(max)` | Yes | The exception's full string representation. |
| `MachineName` | `nvarchar(255)` | No | The machine name captured during log initialization. |

The `Logged` column is created as:

```sql
[Logged] datetime2(0) NOT NULL DEFAULT (SYSUTCDATETIME())
```

The default also applies to direct SQL inserts that omit `Logged`. It does not replace an explicitly supplied timestamp or `NULL`.

`Logged` has no fractional-second storage. SQL Server rounds its generated UTC timestamp: `12:30:00.1234567` becomes `12:30:00`, while `12:30:00.7500000` becomes `12:30:01`. The column does not store a time-zone offset; its values represent UTC by contract. Read and count interval arguments remain UTC values supplied by the caller.

The log table is managed by `SqlLog` and is not registered as a Blueprint entity. The database generates `Id`, and `WriteToLog` does not return it.

## Execution and errors

- Writes are synchronous. A successful call returns after its SQL insert completes; it does not queue the event for later processing.
- Each write, read, or count uses its own SQL connection and parameterized command. An instance can be reused for concurrent calls without sharing a mutable command or connection. Connection pooling remains enabled unless it is disabled in the supplied connection string.
- Log connections do not enlist in ambient application transactions. Rolling back an application transaction does not roll back a log event that was successfully written.
- Invalid arguments and SQL failures propagate to the caller. There is no built-in retry, fallback destination, or suppression of logging failures.
- `SqlLog` does not hold an open connection between calls and does not implement `IDisposable`. It does not require a `using` block or a shutdown flush.

The current API provides no deletion method or automatic retention policy. Deleting log rows remains a separate database operation.

## Related topics

[BlueprintLog](BlueprintLog.md) · [Getting Started](Getting-Started.md) · [SQL Server](Database-SqlServer.md) · [Transactions](Database-Transactions.md) · [Contributing](Contributing.md)
