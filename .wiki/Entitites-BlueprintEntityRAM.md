# BlueprintEntityRAM

`BlueprintEntityRAM` is the base class for SQL-persisted entities that serve eligible queries from an in-memory snapshot. Select it by inheritance, directly or through an intermediate class. Its public query and write methods are documented in the [shared Entity API](Entitites-BlueprintEntity.md#method-index).

## When to use BlueprintEntityRAM

A category list displayed throughout a storefront is a good candidate when it changes infrequently and readers can tolerate a delay before seeing external edits. Budget memory and startup time for the entire table, including every configured translation. A query that displays only twenty categories still uses a cache containing all categories. For a large table usually queried by a single key, consider [BlueprintEntityDB](Entitites-BlueprintEntityDB.md).

## Define and register a RAM entity

The following contract stores one title per configured language.

```csharp
using Blueprint.Entities;
using Blueprint.Entities.Annotations;

/// <summary>Stores a translated category title for the storefront.</summary>
[Entity("GuideCategories")]
public abstract class Category : BlueprintEntityRAM
{
    [Multilingual]
    public abstract string? Title { get; set; }
}
```

Register the type with the application's initialized `BlueprintDatabase`. Before the call returns, Blueprint loads all category rows and their translations into this application instance's cache.

```csharp
database.RegisterEntities(typeof(Category));
```

## Read from RAM or SQL

With `System.Linq` and `System.Collections.Generic` imported, this query reads the first twenty categories with a non-null title in the default language. Outside a transaction, the filter, ordering, and paging run against the RAM snapshot.

```csharp
List<Category> categories = Category.Where(item => item.Title != null).OrderBy(item => item.Title).ThenBy(item => item.ID).Take(20).ToList();
```

Queries that need SQL-specific operations, such as `EF.Functions.Like`, go to SQL Server. A relationship loaded through `[AutoInclude]` uses the related entity's own read source. Inside a Blueprint transaction the entire query, including related entities, reads from SQL so it can see the transaction's uncommitted writes. See [query support and SQL fallback](Database-RAMCaching.md#queries-and-sql-fallback).

## Write and refresh

To rename a category in the default language, use its existing ID and save the edited object.

```csharp
Category? category = Category.SingleOrDefault(item => item.ID == categoryID);
if (category != null)
{
    category.Title = "Audio equipment";
    Category.Update(category);
}
```

`Update` writes to SQL Server and invalidates the local cache so a subsequent cached read synchronizes it. If another application renames the same category directly in SQL, new cached queries see that change after this instance successfully refreshes through SQL Server Change Tracking. A previously returned `Category` keeps its own values. Query again to obtain the refreshed title.

## Cache configuration

`database.RAMCacheRefreshInterval` controls how often each application instance checks for changes. The default is one second. A refresh can take longer if SQL Server is busy or a query is blocked. See [SQL Server requirements and permissions](Database-RAMCaching.md#sql-server-requirements), [running on multiple servers](Database-RAMCaching.md#running-on-multiple-servers), and [refresh failures](Database-RAMCaching.md#refresh-failures-and-lifecycle).
