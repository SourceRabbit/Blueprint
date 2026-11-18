// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Fundamentals;
using System.Globalization;

namespace Blueprint.Localization
{
    /// <summary>Provides language identifiers for multilingual property columns.</summary>
    public static class LanguageColumns
    {
        /// <summary>Uppercases the language suffix for column names independently of culture.</summary>
        /// <param name="language">The column language.</param>
        /// <returns>The uppercase Prefix, empty for the default language.</returns>
        /// <exception cref="ArgumentNullException">No language or Prefix was provided.</exception>
        public static string getColumnSuffix(this BlueprintLanguage language)
        {
            ArgumentNullException.ThrowIfNull(language);
            ArgumentNullException.ThrowIfNull(language.Prefix);
            return language.Prefix.ToUpperInvariant();
        }

        /// <summary>Creates an independent language snapshot and rejects ambiguous mappings.</summary>
        /// <param name="languages">The languages supplied by the application.</param>
        /// <returns>A copy of the validated list in its original order.</returns>
        /// <exception cref="ArgumentNullException">No list was provided.</exception>
        /// <exception cref="ArgumentException">There is a null value, a duplicate ID or prefix, or not exactly one empty Prefix.</exception>
        internal static List<BlueprintLanguage> ValidateLanguages(IEnumerable<BlueprintLanguage> languages)
        {
            ArgumentNullException.ThrowIfNull(languages);
            List<BlueprintLanguage> selectedLanguages = languages.ToList();
            HashSet<long> ids = new HashSet<long>();
            HashSet<string> prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BlueprintLanguage language in selectedLanguages)
            {
                if (language == null || language.Prefix == null || !ids.Add(language.ID) || !prefixes.Add(language.Prefix) || (language.Prefix.Length > 0 && string.IsNullOrWhiteSpace(language.Prefix)))
                {
                    throw new ArgumentException("Languages must have unique IDs and non-null, unique prefixes. Only the default language can have an empty prefix.", nameof(languages));
                }
            }
            if (!prefixes.Contains(string.Empty))
            {
                throw new ArgumentException("Exactly one language must have an empty prefix to identify the default language.", nameof(languages));
            }
            return selectedLanguages;
        }

        /// <summary>Separates the stored CLR property from the multilingual accessor, even when Prefix is empty.</summary>
        /// <param name="propertyName">The original property name.</param>
        /// <param name="languageID">The stable translation ID.</param>
        /// <returns>The internal name of the stored property.</returns>
        internal static string getPropertyName(string propertyName, long languageID)
        {
            return "BlueprintTranslation_" + languageID.ToString("X16", CultureInfo.InvariantCulture) + "_" + propertyName;
        }
    }
}
