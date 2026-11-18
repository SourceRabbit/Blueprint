// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Microsoft.Data.SqlClient;

namespace Blueprint.Tests
{
    /// <summary>Provides shared SQL commands for integration tests in their temporary databases.</summary>
    internal static class SqlTestHelper
    {
        /// <summary>Executes SQL exclusively in the temporary test database.</summary>
        /// <param name="connectionString">The connection string for the test run.</param>
        /// <param name="sql">The SQL statement used by the test.</param>
        internal static void Execute(string connectionString, string sql)
        {
            using SqlConnection connection = new SqlConnection(connectionString);
            connection.Open();
            using SqlCommand command = new SqlCommand(sql, connection);
            command.ExecuteNonQuery();
        }

        /// <summary>Reads a numeric value to verify SQL Server metadata.</summary>
        /// <param name="connectionString">The connection string for the test run.</param>
        /// <param name="sql">The scalar query.</param>
        /// <returns>The value as a long.</returns>
        internal static long Scalar(string connectionString, string sql)
        {
            using SqlConnection connection = new SqlConnection(connectionString);
            connection.Open();
            using SqlCommand command = new SqlCommand(sql, connection);
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }
}
