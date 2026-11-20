// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.Linq.Expressions;

namespace Blueprint.Entities.Triggers
{
    /// <summary>A counter declaration shared by the entity model and SQL schema synchronization.</summary>
    internal sealed record CountTrigger(string Name, Type Source, Type Target, string ForeignKey, string Counter, LambdaExpression? Include);
}
