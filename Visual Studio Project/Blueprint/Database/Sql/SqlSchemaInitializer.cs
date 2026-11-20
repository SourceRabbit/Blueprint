// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database.Sql.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace Blueprint.Database.Sql
{
    /// <summary>Synchronizes the schema through EF Core, applying only changes that preserve data.</summary>
    internal static class SqlSchemaInitializer
    {
        /// <summary>Validates and applies schema changes atomically, serializing concurrent initialization.</summary>
        /// <param name="context">The context with the complete model.</param>
        /// <exception cref="InvalidOperationException">An existing table has an incompatible key or IDENTITY column.</exception>
        internal static void Initialize(BlueprintDatabaseContext context)
        {
            using var transaction = context.Database.BeginTransaction();
            // The shared "Blueprint.Schema" application lock serializes schema synchronization for all entities in this database across application instances.
            // Exclusive mode lets one initializer proceed while others wait up to 30 seconds to acquire the lock before reading the existing schema.
            // Transaction ownership keeps the lock through table, column, index and foreign key changes and releases it automatically on commit or rollback.
            // Nonnegative return codes indicate success; negative codes abort initialization through the THROW below.
            // This lock is acquired only during schema synchronization and uses a separate resource from SqlLog's "Blueprint.Log.Schema" lock.
            context.Database.ExecuteSqlRaw("DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = N'Blueprint.Schema', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; IF @result < 0 THROW 51000, 'Could not acquire the Blueprint schema lock.', 1;");
            var model = context.GetService<IDesignTimeModel>().Model;
            var relationalModel = model.GetRelationalModel();
            var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, relationalModel);
            var tables = ReadObjects(context, "SELECT s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id");
            var indexes = ReadObjects(context, "SELECT s.name, t.name + '.' + i.name FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE i.name IS NOT NULL");
            var foreignKeys = ReadObjects(context, "SELECT s.name, f.name FROM sys.foreign_keys f JOIN sys.schemas s ON s.schema_id = f.schema_id");
            var selected = new List<MigrationOperation>();
            var columnChanges = new List<MigrationOperation>();
            var sqlHelper = context.GetService<ISqlGenerationHelper>();

            foreach (var table in relationalModel.Tables.Where(table => tables.Contains((table.Schema ?? "dbo", table.Name))))
            {
                ValidateExistingTable(context, table, sqlHelper);
            }
            foreach (var table in operations.OfType<CreateTableOperation>().Where(table => tables.Contains((table.Schema ?? "dbo", table.Name))))
            {
                columnChanges.AddRange(SqlColumnUpdater.Plan(context, table));
                selected.AddRange(table.ForeignKeys.Where(key => !foreignKeys.Contains((key.Schema ?? "dbo", key.Name))));
            }

            foreach (var operation in operations)
            {
                switch (operation)
                {
                    case CreateTableOperation table when !tables.Contains((table.Schema ?? "dbo", table.Name)):
                        selected.Add(table);
                        break;
                    case CreateIndexOperation index when !indexes.Contains((index.Schema ?? "dbo", index.Table + "." + index.Name)):
                        selected.Add(index);
                        break;
                    case AddForeignKeyOperation foreignKey when !foreignKeys.Contains((foreignKey.Schema ?? "dbo", foreignKey.Name)):
                        selected.Add(foreignKey);
                        break;
                }
            }

            var generator = context.GetService<IMigrationsSqlGenerator>();
            foreach (var command in generator.Generate(operations.OfType<EnsureSchemaOperation>().ToList()))
            {
                context.Database.ExecuteSqlRaw(command.CommandText);
            }
            SqlEntityIds.Initialize(context, relationalModel, tables);
            foreach (var command in generator.Generate(columnChanges))
            {
                context.Database.ExecuteSqlRaw(command.CommandText);
            }
            var ordered = selected.Where(operation => operation is not AddForeignKeyOperation).Concat(selected.OfType<AddForeignKeyOperation>()).ToList();
            foreach (var command in generator.Generate(ordered, model))
            {
                context.Database.ExecuteSqlRaw(command.CommandText);
            }
            SqlEntityTriggers.Synchronize(context);
            SqlChangeTracking.InitializeTables(context);
            transaction.Commit();
        }

        /// <summary>Reads existing object schemas and names from system catalogs.</summary>
        /// <param name="context">The transaction context.</param>
        /// <param name="sql">The fixed metadata query.</param>
        /// <returns>The objects present in the database.</returns>
        private static HashSet<(string Schema, string Name)> ReadObjects(DbContext context, string sql)
        {
            using var command = CreateCommand(context, sql);
            using var reader = command.ExecuteReader();
            var result = new HashSet<(string, string)>();
            while (reader.Read())
            {
                result.Add((reader.GetString(0), reader.GetString(1)));
            }
            return result;
        }

        /// <summary>Rejects incompatible existing keys without modifying tables or data.</summary>
        /// <param name="context">The transaction context.</param>
        /// <param name="table">The expected table.</param>
        /// <param name="sqlHelper">The mechanism for safely quoting identifiers.</param>
        /// <exception cref="InvalidOperationException">The key requires an explicit migration.</exception>
        private static void ValidateExistingTable(DbContext context, ITable table, ISqlGenerationHelper sqlHelper)
        {
            using var command = CreateCommand(context, "SELECT c.name, c.is_identity FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.is_primary_key = 1 AND i.object_id = OBJECT_ID(@table) ORDER BY ic.key_ordinal");
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = sqlHelper.DelimitIdentifier(table.Name, table.Schema);
            command.Parameters.Add(parameter);
            using var reader = command.ExecuteReader();
            var columns = new List<string>();
            var hasIdentity = false;
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
                hasIdentity |= reader.GetBoolean(1);
            }
            if (hasIdentity || table.PrimaryKey == null || !columns.SequenceEqual(table.PrimaryKey.Columns.Select(column => column.Name), StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Existing table '{table.Schema}.{table.Name}' has an incompatible primary key or IDENTITY column. Apply an explicit schema migration before initializing Blueprint.");
            }
        }

        /// <summary>Creates a command on EF Core's open connection and transaction.</summary>
        /// <param name="context">The context managing the connection.</param>
        /// <param name="sql">The SQL to execute.</param>
        /// <returns>The command, which the caller must dispose.</returns>
        internal static DbCommand CreateCommand(DbContext context, string sql)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = sql;
            return command;
        }

        /// <summary>Executes DDL with already delimited identifiers and validated numeric literals in the shared transaction.</summary>
        /// <param name="context">The initialization context.</param>
        /// <param name="sql">The SQL, without unprocessed application values.</param>
        internal static void ExecuteCommand(DbContext context, string sql)
        {
            using var command = CreateCommand(context, sql);
            command.ExecuteNonQuery();
        }
    }
}
