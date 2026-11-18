// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;

namespace Blueprint.Database.Sql
{
    /// <summary>Allows only SQL type conversions that preserve all values representable by the original field.</summary>
    internal static class SqlColumnType
    {
        /// <summary>Normalizes equivalent SQL type declarations, filling in default parameters.</summary>
        /// <param name="type">The SQL type declaration.</param>
        /// <returns>The normalized declaration.</returns>
        internal static string Normalize(string type)
        {
            var normalized = Regex.Replace(type.ToLowerInvariant(), @"[\s\[\]]", string.Empty).Replace("numeric", "decimal");
            var floatingPoint = Regex.Match(normalized, @"^float\((\d+)\)$");
            if (floatingPoint.Success && int.TryParse(floatingPoint.Groups[1].Value, out var bits) && bits is >= 1 and <= 53)
            {
                return bits <= 24 ? "float(24)" : "float(53)";
            }
            return normalized switch
            {
                "timestamp" => "rowversion",
                "float" => "float(53)",
                "real" => "float(24)",
                "decimal" => "decimal(18,0)",
                "datetime2" => "datetime2(7)",
                "datetimeoffset" => "datetimeoffset(7)",
                "time" => "time(7)",
                "varchar" or "nvarchar" or "char" or "nchar" or "binary" or "varbinary" => normalized + "(1)",
                _ => normalized
            };
        }

        /// <summary>Checks for safe increases in length, precision, or range without trial conversions of stored data.</summary>
        /// <param name="source">The existing SQL type.</param>
        /// <param name="target">The requested SQL type.</param>
        /// <returns>True only for a conversion proven to preserve existing values.</returns>
        internal static bool CanWiden(string source, string target)
        {
            source = Normalize(source);
            target = Normalize(target);
            if (source == target)
            {
                return true;
            }
            var sourceMatch = Regex.Match(source, @"^(\w+)(?:\((max|\d+)(?:,(\d+))?\))?$");
            var targetMatch = Regex.Match(target, @"^(\w+)(?:\((max|\d+)(?:,(\d+))?\))?$");
            if (!sourceMatch.Success || !targetMatch.Success)
            {
                return false;
            }
            var sourceName = sourceMatch.Groups[1].Value;
            var targetName = targetMatch.Groups[1].Value;
            var sourceSize = GetSize(sourceMatch.Groups[2].Value);
            var targetSize = GetSize(targetMatch.Groups[2].Value);
            var integers = new[] { "bit", "tinyint", "smallint", "int", "bigint" };
            var sourceInteger = Array.IndexOf(integers, sourceName);
            var targetInteger = Array.IndexOf(integers, targetName);
            if (sourceInteger >= 0 && targetInteger >= sourceInteger)
            {
                return true;
            }
            if (sourceName == targetName)
            {
                if (sourceName == "decimal")
                {
                    var sourceScale = GetSize(sourceMatch.Groups[3].Value);
                    var targetScale = GetSize(targetMatch.Groups[3].Value);
                    return targetScale >= sourceScale && targetSize - targetScale >= sourceSize - sourceScale;
                }
                return new[] { "varchar", "nvarchar", "varbinary", "datetime2", "datetimeoffset", "time", "float" }.Contains(sourceName) && targetSize >= sourceSize;
            }
            if (sourceInteger >= 0 && targetName == "decimal")
            {
                var digits = new[] { 1, 3, 5, 10, 19 };
                return targetSize - GetSize(targetMatch.Groups[3].Value) >= digits[sourceInteger];
            }
            return sourceName == "varchar" && targetName == "nvarchar" && sourceSize != int.MaxValue && targetSize >= sourceSize;
        }

        /// <summary>Converts a length or precision parameter to a numeric limit.</summary>
        /// <param name="value">A number, max, or an empty parameter.</param>
        /// <returns>The numeric limit.</returns>
        private static int GetSize(string value)
        {
            return value == "max" ? int.MaxValue : int.TryParse(value, out var size) ? size : 0;
        }
    }
}
