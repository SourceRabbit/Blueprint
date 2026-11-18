// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Globalization;

namespace Blueprint.Database.Sql
{
    /// <summary>Maintains an increasing sequence per table above the IDs already stored.</summary>
    internal static class SqlEntityIds
    {
        /// <summary>Creates sequences and synchronizes their next values, requiring an increment of one and no cycling.</summary>
        /// <param name="context">The initialization transaction context.</param>
        /// <param name="model">The relational model.</param>
        /// <param name="tables">The tables that already exist.</param>
        /// <exception cref="InvalidOperationException">An existing sequence has an incompatible increment or allows cycling.</exception>
        /// <exception cref="DbException">Reading, creating, or synchronizing a sequence fails.</exception>
        internal static void Initialize(DbContext context, IRelationalModel model, HashSet<(string Schema, string Name)> tables)
        {
            ISqlGenerationHelper helper = context.GetService<ISqlGenerationHelper>();
            foreach (ITable table in model.Tables)
            {
                string schema = table.Schema ?? "dbo";
                string sequenceName = helper.DelimitIdentifier(table.Name + "_ID", schema);
                long maximum = 0;
                if (tables.Contains((schema, table.Name)))
                {
                    using DbCommand maximumCommand = SqlSchemaInitializer.CreateCommand(context, $"SELECT COALESCE(MAX(CAST({helper.DelimitIdentifier(table.PrimaryKey!.Columns.Single().Name)} AS bigint)), 0) FROM {helper.DelimitIdentifier(table.Name, schema)} WITH (TABLOCKX, HOLDLOCK)");
                    maximum = Convert.ToInt64(maximumCommand.ExecuteScalar());
                }
                using DbCommand read = SqlSchemaInitializer.CreateCommand(context, "SELECT CAST(COALESCE(last_used_value, start_value) AS bigint), CAST(increment AS bigint), is_cycling, CASE WHEN last_used_value IS NULL THEN 0 ELSE 1 END FROM sys.sequences WHERE object_id = OBJECT_ID(@name)");
                DbParameter parameter = read.CreateParameter();
                parameter.ParameterName = "@name";
                parameter.Value = sequenceName;
                read.Parameters.Add(parameter);
                long? current = null;
                long increment = 1;
                bool cycling = false;
                bool used = false;
                using (DbDataReader reader = read.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        current = reader.GetInt64(0);
                        increment = reader.GetInt64(1);
                        cycling = reader.GetBoolean(2);
                        used = reader.GetInt32(3) == 1;
                    }
                }
                if (current == null)
                {
                    SqlSchemaInitializer.ExecuteCommand(context, $"CREATE SEQUENCE {sequenceName} AS bigint START WITH {(maximum + 1).ToString(CultureInfo.InvariantCulture)} INCREMENT BY 1 NO CYCLE;");
                    continue;
                }
                if (increment != 1 || cycling)
                {
                    throw new InvalidOperationException($"Sequence '{sequenceName}' must use INCREMENT BY 1 and NO CYCLE. Automatic conversion of incompatible sequences is not supported.");
                }
                long next = current.Value + (used ? 1 : 0);
                if (next <= maximum)
                {
                    next = maximum + 1;
                    SqlSchemaInitializer.ExecuteCommand(context, $"ALTER SEQUENCE {sequenceName} RESTART WITH {next.ToString(CultureInfo.InvariantCulture)};");
                }
            }
        }
    }
}
