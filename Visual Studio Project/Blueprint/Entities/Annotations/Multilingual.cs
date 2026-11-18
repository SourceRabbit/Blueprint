// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Entities.Annotations
{
    /// <summary>Declares that a property stores a separate value for each language.</summary>
    /// <remarks>Applies to string properties and generates one column per BlueprintLanguage configured during initialization. CurrentLanguage selects the value by ID.</remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class Multilingual : Attribute
    {
    }
}
