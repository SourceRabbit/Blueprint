// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace Blueprint.Database.Sql
{
    /// <summary>Enables the SQL Server features required by selected RAM-cached entities.</summary>
    internal static class SqlChangeTracking
    {
        /// <summary>Enables Change Tracking and snapshot isolation without changing existing retention settings.</summary>
        /// <param name="context">The model context before the initialization transaction.</param>
        /// <exception cref="DbException">ALTER DATABASE permissions are missing or communication fails.</exception>
        internal static void InitializeDatabase(DbContext context)
        {
            if (!GetCachedTypes(context).Any())
            {
                return;
            }
            context.Database.OpenConnection();
            try
            {
                string databaseName = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Database.GetDbConnection().Database);
                using DbCommand command = context.Database.GetDbConnection().CreateCommand();
                command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
                command.CommandText = $"""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource = N'Blueprint.ChangeTracking', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 30000;
                    IF @result < 0 THROW 51000, 'Could not acquire the Blueprint change tracking lock.', 1;
                    BEGIN TRY
                        IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_databases WHERE database_id = DB_ID())
                            ALTER DATABASE {databaseName} SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON);
                        IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND snapshot_isolation_state <> 1)
                            ALTER DATABASE {databaseName} SET ALLOW_SNAPSHOT_ISOLATION ON;
                        EXEC sys.sp_releaseapplock @Resource = N'Blueprint.ChangeTracking', @LockOwner = 'Session';
                    END TRY
                    BEGIN CATCH
                        EXEC sys.sp_releaseapplock @Resource = N'Blueprint.ChangeTracking', @LockOwner = 'Session';
                        THROW;
                    END CATCH;
                    """;
                command.ExecuteNonQuery();
            }
            finally
            {
                context.Database.CloseConnection();
            }
        }

        /// <summary>Enables tracking only on declared tables within the schema transaction.</summary>
        /// <param name="context">The context after tables and primary keys have been created.</param>
        /// <exception cref="DbException">ALTER TABLE permissions are missing or DDL execution fails.</exception>
        internal static void InitializeTables(DbContext context)
        {
            ISqlGenerationHelper sqlHelper = context.GetService<ISqlGenerationHelper>();
            foreach (IEntityType entityType in GetCachedTypes(context))
            {
                string tableName = sqlHelper.DelimitIdentifier(entityType.GetTableName()!, entityType.GetSchema());
                using DbCommand command = SqlSchemaInitializer.CreateCommand(context, $"IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(@table)) ALTER TABLE {tableName} ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF);");
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = "@table";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Finds mappings only for entity classes whose inheritance selects RAM caching.</summary>
        /// <param name="context">The context with registered types.</param>
        /// <returns>The EF mappings of the selected entities.</returns>
        private static IEnumerable<IEntityType> GetCachedTypes(DbContext context)
        {
            BlueprintDatabaseContext entityContext = (BlueprintDatabaseContext)context;
            return entityContext.EntityTypes.GetRAMCachedContracts().Select(contract => context.Model.FindEntityType(entityContext.EntityTypes.Types[contract])!);
        }
    }
}
