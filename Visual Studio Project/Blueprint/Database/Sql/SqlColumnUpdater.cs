// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;

namespace Blueprint.Database.Sql
{
    /// <summary>Compares actual SQL Server columns with the model and plans safe changes.</summary>
    internal static class SqlColumnUpdater
    {
        /// <summary>Adds new columns and safe changes, leaving columns outside the model unchanged.</summary>
        /// <param name="context">The initialization transaction context.</param>
        /// <param name="table">The desired table schema.</param>
        /// <returns>The validated table changes.</returns>
        /// <exception cref="InvalidOperationException">The change could damage data or requires an explicit initial value.</exception>
        internal static IReadOnlyList<MigrationOperation> Plan(DbContext context, CreateTableOperation table)
        {
            var helper = context.GetService<ISqlGenerationHelper>();
            var tableName = helper.DelimitIdentifier(table.Name, table.Schema);
            var columns = ReadColumns(context, tableName);
            var changes = new List<MigrationOperation>();
            bool? hasRows = null;
            foreach (var target in table.Columns.OrderBy(column => column.ComputedColumnSql != null))
            {
                if (!columns.TryGetValue(target.Name, out var source))
                {
                    hasRows ??= LockTable(context, tableName);
                    if (hasRows.Value && !target.IsNullable && target.DefaultValue == null && target.DefaultValueSql == null && !target.IsRowVersion && target.ComputedColumnSql == null)
                    {
                        throw UnsafeChange(table, target, "A new required column in a populated table needs [DefaultValue(...)] or must be nullable.");
                    }
                    changes.Add(target);
                    continue;
                }

                var typeChanged = SqlColumnType.Normalize(source.ColumnType!) != SqlColumnType.Normalize(target.ColumnType!);
                var nullabilityChanged = source.IsNullable != target.IsNullable;
                var expectedDefault = GetDefaultSql(context, target);
                var defaultChanged = NormalizeDefault(source.DefaultValueSql) != NormalizeDefault(expectedDefault);
                if (source.ComputedColumnSql != null || target.ComputedColumnSql != null)
                {
                    if (source.ComputedColumnSql != target.ComputedColumnSql)
                    {
                        throw UnsafeChange(table, target, "Computed-column changes need an explicit migration.");
                    }
                    continue;
                }
                if (!typeChanged && !nullabilityChanged && !defaultChanged)
                {
                    continue;
                }
                hasRows ??= LockTable(context, tableName);
                if (source.IsRowVersion || target.IsRowVersion || source["Blueprint:SpecialColumn"] is true)
                {
                    throw UnsafeChange(table, target, "Generated, identity, rowversion and user-defined columns cannot be altered automatically.");
                }
                if (typeChanged && !SqlColumnType.CanWiden(source.ColumnType!, target.ColumnType!))
                {
                    throw UnsafeChange(table, target, $"Conversion from '{source.ColumnType}' to '{target.ColumnType}' could truncate, round or reinterpret values.");
                }
                if (source.IsNullable && !target.IsNullable)
                {
                    using var check = SqlSchemaInitializer.CreateCommand(context, $"SELECT TOP (1) 1 FROM {tableName} WHERE {helper.DelimitIdentifier(target.Name)} IS NULL");
                    if (check.ExecuteScalar() != null)
                    {
                        throw UnsafeChange(table, target, "Existing NULL values must be filled explicitly before making this column required.");
                    }
                }

                var alteration = new AlterColumnOperation
                {
                    Name = target.Name,
                    Schema = target.Schema,
                    Table = target.Table,
                    ClrType = target.ClrType,
                    ColumnType = target.ColumnType,
                    IsUnicode = target.IsUnicode,
                    IsFixedLength = target.IsFixedLength,
                    MaxLength = target.MaxLength,
                    Precision = target.Precision,
                    Scale = target.Scale,
                    IsNullable = target.IsNullable,
                    DefaultValue = target.DefaultValue,
                    DefaultValueSql = target.DefaultValueSql,
                    Collation = source.Collation,
                    OldColumn = source
                };
                changes.Add(alteration);
            }
            return changes;
        }

        /// <summary>Reads complete physical column metadata without assuming a migration history exists.</summary>
        /// <param name="context">The active context.</param>
        /// <param name="tableName">The delimited table name.</param>
        /// <returns>The metadata indexed by column name.</returns>
        internal static Dictionary<string, AddColumnOperation> ReadColumns(DbContext context, string tableName)
        {
            using var command = SqlSchemaInitializer.CreateCommand(context, "SELECT c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable, d.definition, c.collation_name, cc.definition, c.is_identity, t.is_user_defined, c.generated_always_type, c.encryption_type FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id LEFT JOIN sys.default_constraints d ON c.default_object_id = d.object_id LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id WHERE c.object_id = OBJECT_ID(@table)");
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);
            using var reader = command.ExecuteReader();
            var result = new Dictionary<string, AddColumnOperation>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var length = (int)reader.GetInt16(2);
                var precision = reader.GetByte(3);
                var scale = reader.GetByte(4);
                var size = length == -1 ? "max" : (name.StartsWith('n') ? length / 2 : length).ToString(CultureInfo.InvariantCulture);
                var type = name switch
                {
                    "nvarchar" or "nchar" or "varchar" or "char" or "varbinary" or "binary" => $"{name}({size})",
                    "decimal" or "numeric" => $"decimal({precision},{scale})",
                    "datetime2" or "datetimeoffset" or "time" => $"{name}({scale})",
                    "float" => $"float({precision})",
                    _ => name
                };
                var column = new AddColumnOperation
                {
                    Name = reader.GetString(0),
                    ColumnType = type,
                    ClrType = typeof(object),
                    IsNullable = reader.GetBoolean(5),
                    DefaultValueSql = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Collation = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ComputedColumnSql = reader.IsDBNull(8) ? null : reader.GetString(8),
                    IsRowVersion = name is "timestamp" or "rowversion"
                };
                column["Blueprint:SpecialColumn"] = reader.GetBoolean(9) || reader.GetBoolean(10) || reader.GetByte(11) != 0 || !reader.IsDBNull(12);
                result.Add(column.Name, column);
            }
            return result;
        }

        /// <summary>Blocks concurrent writes until commit so that NULL and empty-table checks remain valid.</summary>
        /// <param name="context">The transaction context.</param>
        /// <param name="tableName">The delimited table name.</param>
        /// <returns>Whether the table already contains data.</returns>
        private static bool LockTable(DbContext context, string tableName)
        {
            using var command = SqlSchemaInitializer.CreateCommand(context, $"SELECT TOP (1) 1 FROM {tableName} WITH (TABLOCKX, HOLDLOCK)");
            return command.ExecuteScalar() != null;
        }

        /// <summary>Formats the desired default using SQL Server type mapping.</summary>
        /// <param name="context">The provider context.</param>
        /// <param name="column">The desired column.</param>
        /// <returns>The SQL literal or expression for the default.</returns>
        private static string? GetDefaultSql(DbContext context, ColumnOperation column)
        {
            return column.DefaultValueSql ?? (column.DefaultValue == null ? null : context.GetService<IRelationalTypeMappingSource>().FindMapping(column.ColumnType!)!.GenerateSqlLiteral(column.DefaultValue));
        }

        /// <summary>Removes only outer parentheses from default expressions in system catalogs.</summary>
        /// <param name="value">The SQL default.</param>
        /// <returns>The comparable expression.</returns>
        private static string? NormalizeDefault(string? value)
        {
            value = value?.Trim();
            while (value?.StartsWith('(') == true && value.EndsWith(')'))
            {
                value = value[1..^1].Trim();
            }
            return value;
        }

        /// <summary>Identifies the exact field requiring an explicit migration to protect data.</summary>
        /// <param name="table">The table being examined.</param>
        /// <param name="column">The field being changed.</param>
        /// <param name="reason">The rejection reason.</param>
        /// <returns>The exception with corrective information.</returns>
        private static InvalidOperationException UnsafeChange(CreateTableOperation table, ColumnOperation column, string reason)
        {
            return new InvalidOperationException($"Unsafe automatic schema change for '{table.Schema}.{table.Name}.{column.Name}'. {reason} No schema changes will be committed.");
        }
    }
}
