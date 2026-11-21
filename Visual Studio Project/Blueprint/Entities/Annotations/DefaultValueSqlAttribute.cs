// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Entities.Annotations
{
    /// <summary>Supplies the SQL expression used as the default when an insert omits the column.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class DefaultValueSqlAttribute : Attribute
    {
        /// <summary>Specifies the database expression for the column's default constraint.</summary>
        /// <param name="sql">The SQL default expression, such as SYSUTCDATETIME().</param>
        /// <exception cref="ArgumentException">The expression is empty or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The expression is null.</exception>
        public DefaultValueSqlAttribute(string sql)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sql);
            Sql = sql;
        }

        /// <summary>The expression evaluated by the database on insert.</summary>
        public string Sql { get; }
    }
}
