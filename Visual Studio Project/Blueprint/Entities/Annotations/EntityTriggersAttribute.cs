// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Entities.Annotations
{
    /// <summary>Marks a public static void method that declares an entity's database triggers.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class EntityTriggersAttribute : Attribute
    {
    }
}
