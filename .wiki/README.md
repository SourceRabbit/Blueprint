# Blueprint User Manual

Blueprint is a backend application framework for .NET, **designed to provide a stable, consistent API across different underlying technologies**. Applications interact with Blueprint’s contracts and services, allowing infrastructure choices to evolve independently of business logic.

This manual describes the consumer API available in this repository. Examples target **.NET 10 and C# 14**. Start with [Getting Started](Getting-Started.md) to add Blueprint to your project, then follow [EShop - Checkout Tutorial](Database-Tutorial-EShop-Checkout.md) for a complete application example.

## Getting Started

- [Getting Started](Getting-Started.md): add Blueprint to your project using a project or DLL reference and configure its dependencies.

## Blueprint Database

- [BlueprintDatabase](Database-BlueprintDatabase.md): understand the abstract base class, shared configuration, entity registration, and application lifetime.
  - [SqlDatabase](Database-SqlServer.md): database provider for Microsoft SQL Server.
  - [MySQL](Database-MySqlServer.md): Coming Soon.
- Entities
  - [BlueprintEntity, BlueprintEntityDB, and BlueprintEntityRAM](Database-Entities-BlueprintEntity.md): class-based entities, storage/cache selection, and migration from interfaces.
  - [Data Annotations](Database-Entities-Annotations.md): configure fields, indexes, generated values, and concurrency tokens.
  - [Relationships](Data-Entities-Relationships.md): define foreign keys, reference properties, inverse collections, and automatic loading.
  - [Schema Synchronization](Database-Entities-Schema-Synchronization.md): understand which database changes registration applies automatically.
- Reading and Writing Data
  - [Query Executors](Database-Query-Executors.md): the shared LINQ API and per-entity DB/RAM execution, including mixed AutoInclude graphs.
  - [Queries and CRUD](Database-Queries-and-CRUD.md): filter, sort, page, insert, update, and delete.
  - [Transactions](Database-Transactions.md): group writes, prepare operations, validate before commit, and handle rollback or cancellation.
- Localization
  - [Multilingual Entities](Database-Multilingual-Entities.md): configure languages and read or update translations.
- Caching
  - [RAM Caching](Database-RAM-Caching.md): cache selected tables and synchronize committed SQL changes.
- Tutorials
  - [EShop - Checkout Tutorial](Database-Tutorial-EShop-Checkout.md): create a website user, add an iPhone to the basket, and complete checkout with a transaction that saves the order, reduces stock, and clears the basket.

## Background Tasks

- [Background Tasks](Background-Tasks.md): register work, use interval or daily schedules, and control execution.

## Log

- [BlueprintLog](BlueprintLog.md): understand the abstract `BlueprintLog` API, log levels, results, and available or planned implementations.
  - [SqlLog](SqlLog.md): log provider for Microsoft SQL Server.
  - [MySqlLog (Coming Soon)](MySqlLog.md)

## Help

- [Troubleshooting](Troubleshooting.md): identify common configuration, mapping, query, and transaction problems.
- [Contributing](Contributing.md): coding style, naming rules, documentation comments, and contribution guidelines.

## Reading the examples

Use `using Blueprint.Entities;` for the static Entity API. Most examples use the [Product contract](Database-Tutorial-EShop-Checkout.md#define-a-contract) from the EShop checkout tutorial and assume its registration and an active `BlueprintDatabase`. A page that requires another contract shows that contract explicitly.

Code blocks are independent examples unless stated otherwise. Replace sample connection strings and IDs with values for your application. All database writes should be explicit: editing a loaded object's properties does not save it automatically.

[Repository overview](../Readme.md)
