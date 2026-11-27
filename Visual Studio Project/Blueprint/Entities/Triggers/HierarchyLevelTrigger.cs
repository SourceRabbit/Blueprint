// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Entities.Triggers
{
    /// <summary>A stored depth derived from the nullable parent key of a self-referencing entity.</summary>
    internal sealed record HierarchyLevelTrigger(string Name, Type Entity, string ParentID, string Level);
}
