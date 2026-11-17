using System.Globalization;

namespace Blueprint.Database.Queries
{
    /// <summary>Provides explicit case and accent options for substring and prefix searches in memory and Blueprint queries.</summary>
    public static class TextSearch
    {
        /// <summary>Searches for a literal substring using the invariant linguistic comparison and explicit case and accent options.</summary>
        /// <param name="source">The text to search, or null.</param>
        /// <param name="value">The literal substring to find, or null.</param>
        /// <param name="options">None, IgnoreCase, IgnoreNonSpace, or IgnoreCase combined with IgnoreNonSpace.</param>
        /// <returns>False if either string is null; otherwise, whether the substring matches. An empty value matches any non-null source.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The options include an unsupported flag.</exception>
        /// <remarks>Blueprint translates this overload to SQL Server with an explicit search collation. Existing string.Contains overloads are unchanged.</remarks>
        public static bool Contains(this string? source, string? value, CompareOptions options)
        {
            ValidateOptions(options);
            return source != null && value != null && CultureInfo.InvariantCulture.CompareInfo.IndexOf(source, value, options) >= 0;
        }

        /// <summary>Checks a literal prefix using the invariant linguistic comparison and explicit case and accent options.</summary>
        /// <param name="source">The text to search, or null.</param>
        /// <param name="value">The literal prefix to find, or null.</param>
        /// <param name="options">None, IgnoreCase, IgnoreNonSpace, or IgnoreCase combined with IgnoreNonSpace.</param>
        /// <returns>False if either string is null; otherwise, whether the prefix matches. An empty value matches any non-null source.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The options include an unsupported flag.</exception>
        /// <remarks>Blueprint translates this overload to SQL Server with an explicit search collation. Existing string.StartsWith overloads are unchanged.</remarks>
        public static bool StartsWith(this string? source, string? value, CompareOptions options)
        {
            ValidateOptions(options);
            return source != null && value != null && CultureInfo.InvariantCulture.CompareInfo.IsPrefix(source, value, options);
        }

        /// <summary>Restricts comparison options to the case and accent combinations supported by both execution paths.</summary>
        /// <param name="options">The requested comparison options.</param>
        /// <exception cref="ArgumentOutOfRangeException">An option other than IgnoreCase or IgnoreNonSpace is present.</exception>
        internal static void ValidateOptions(CompareOptions options)
        {
            if ((options & ~(CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), options, "Use None, IgnoreCase, IgnoreNonSpace, or IgnoreCase | IgnoreNonSpace.");
            }
        }
    }
}
