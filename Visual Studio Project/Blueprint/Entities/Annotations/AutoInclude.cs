// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System;

namespace Blueprint.Entities.Annotations
{
    /// <summary>
    /// Requests that Blueprint collections load this EF Core relationship along with complete entities.
    /// Also follows AutoInclude relationships on related entities, stopping when a type repeats within the same path.
    /// Does not apply to Select, Count, or Any and does not enable relationship persistence.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AutoInclude : Attribute
    {
    }
}
