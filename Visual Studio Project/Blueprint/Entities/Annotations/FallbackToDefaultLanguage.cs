// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Entities.Annotations
{
    /// <summary>Returns the default-language value when the selected translation is null or empty.</summary>
    /// <remarks>Requires Multilingual. Applies only to reads; stored translations and CurrentLanguage remain unchanged. Whitespace is not treated as empty.</remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class FallbackToDefaultLanguage : Attribute
    {
    }
}
