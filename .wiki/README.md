# Blueprint User Manual

<img src="https://raw.githubusercontent.com/SourceRabbit/Blueprint/main/Assets/BlueprintLogo.png" alt="Blueprint logo" width="120" align="left">

Blueprint is a backend application framework for .NET, **designed to provide a stable, consistent API across different underlying technologies**. Applications interact with Blueprint’s contracts and services, allowing infrastructure choices to evolve independently of business logic.

This manual describes the consumer API available in this repository. Examples target **.NET 10 and C# 14**. Start with [Getting Started](GettingStarted.md) to add Blueprint to your project, then follow [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md) for a complete application example.

<br clear="left">

## Getting Started

- [Getting Started](GettingStarted.md): install Blueprint from NuGet or use the DLL and PDB from the latest GitHub release.

## Entities

- [BlueprintEntity](Entitites-BlueprintEntity.md) describes the entity contract, inherited properties, and shared Entity API.
  - [BlueprintEntityDB](Entitites-BlueprintEntityDB.md) reads from and writes to SQL.
  - [BlueprintEntityRAM](Entitites-BlueprintEntityRAM.md) serves eligible queries from RAM and uses SQL Server Change Tracking to synchronize the cache.
- [Data Annotations](Entitites-DataAnnotations.md): configure fields, indexes, generated values, and concurrency tokens.
  - [Relationships](Entitites-Relationships.md): define foreign keys, reference properties, inverse collections, and automatic loading.
  - [Multilingual Entities](Entitites-MultilingualEntities.md): configure languages and read or update translations.
- [Entity Triggers](Entitites-EntityTriggers.md) shows how to maintain counts and calculated values from C# entity declarations.

## Blueprint Database

- [BlueprintDatabase](Database-BlueprintDatabase.md): understand the abstract base class, shared configuration, entity registration, and application lifetime.
  - [SqlDatabase](Database-SqlDatabase.md): database provider for Microsoft SQL Server.
  - [MySQL](Database-MySqlServer.md): Coming Soon.
- Entity Mapping
  - [Schema Synchronization](Database-SchemaSynchronization.md): understand which database changes registration applies automatically.
- Reading and Writing Data
  - [Query Executors](Database-QueryExecutors.md): the shared LINQ API and per-entity DB/RAM execution, including mixed AutoInclude graphs.
  - [Transactions](Database-Transactions.md): group writes, prepare operations, validate before commit, and handle rollback or cancellation.
- Caching
  - [RAM Caching](Database-RAMCaching.md): cache selected tables and synchronize committed SQL changes.
- Tutorials
  - [EShop - Checkout Tutorial](Database-EShopCheckoutTutorial.md): create a website user, add an iPhone to the basket, and complete checkout with a transaction that saves the order, reduces stock, and clears the basket.

## Background Tasks

- [Background Tasks](BackgroundTasks.md): register work, use interval or daily schedules, and control execution.

## Email

- **Messages**
  - [BlueprintEmail](Email-BlueprintEmail.md) describes the sender address, recipients, subject, and body of a message.
  - [BlueprintEmailAttachment](Email-BlueprintEmail.md#blueprintemailattachment) describes an attached file, its contents, and its MIME type.
- **Senders**
  - [BlueprintEmailSender](Email-BlueprintEmailSender.md) defines the shared methods for synchronous and asynchronous email submission.
  - [SMTPEmailSender](Email-SMTPEmailSender.md) covers SMTP connection settings, authentication, and sending an invoice by email.

## Log

- [BlueprintLog](Log-BlueprintLog.md): understand the abstract `BlueprintLog` API, log levels, results, and available or planned implementations.
  - [SqlLog](Log-SqlLog.md): log provider for Microsoft SQL Server.
  - [MySqlLog (Coming Soon)](Log-MySqlLog.md)

## Help

- [Troubleshooting](Troubleshooting.md): identify common configuration, mapping, query, and transaction problems.
- [Contributing](Contributing.md): coding style, naming rules, documentation comments, and contribution guidelines.

## Reading the examples

Use `using Blueprint.Entities;` for the static Entity API. Most examples use the [Product contract](Database-EShopCheckoutTutorial.md#define-a-contract) from the EShop checkout tutorial and assume its registration and an active `BlueprintDatabase`. A page that requires another contract shows that contract explicitly.

Code blocks are independent examples unless stated otherwise. Replace sample connection strings and IDs with values for your application. All database writes should be explicit: editing a loaded object's properties does not save it automatically.

[Repository overview](../Readme.md)
