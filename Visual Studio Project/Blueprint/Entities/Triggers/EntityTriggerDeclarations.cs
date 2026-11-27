// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities.Annotations;
using System.Reflection;

namespace Blueprint.Entities.Triggers
{
    /// <summary>Collects the count and value rules declared by the registered entity contracts.</summary>
    internal sealed class EntityTriggerDeclarations
    {
        /// <summary>Invokes attributed methods once per entity model and validates counter ownership.</summary>
        /// <param name="contracts">The registered contracts including related entities.</param>
        /// <returns>The validated declarations.</returns>
        /// <exception cref="InvalidOperationException">A declaration has an invalid signature, target or duplicate counter.</exception>
        internal static EntityTriggerDeclarations Discover(IEnumerable<Type> contracts)
        {
            HashSet<Type> registered = contracts.ToHashSet();
            EntityTriggerDeclarations rules = new EntityTriggerDeclarations();
            foreach (Type contract in registered)
            {
                foreach (MethodInfo method in contract.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!method.IsDefined(typeof(EntityTriggersAttribute), false))
                    {
                        continue;
                    }
                    Type builder = typeof(EntityTriggerBuilder<>).MakeGenericType(contract);
                    ParameterInfo[] parameters = method.GetParameters();
                    if (!method.IsPublic || !method.IsStatic || method.IsGenericMethod || method.ReturnType != typeof(void) || parameters.Length != 1 || parameters[0].ParameterType != builder)
                    {
                        throw new InvalidOperationException($"'{contract.Name}.{method.Name}' must be public static void and accept an EntityTriggerBuilder<{contract.Name}>.");
                    }
                    object instance = Activator.CreateInstance(builder, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { rules }, null)!;
                    method.Invoke(null, new object[] { instance });
                }
            }
            HashSet<(Type, string)> maintained = new HashSet<(Type, string)>();
            foreach (CountTrigger rule in rules.Counts)
            {
                if (!registered.Contains(rule.Target) || !maintained.Add((rule.Target, rule.Counter)))
                {
                    throw new InvalidOperationException($"Trigger '{rule.Name}' must own a distinct counter on a registered target entity.");
                }
            }
            foreach (HierarchyLevelTrigger rule in rules.HierarchyLevels)
            {
                if (!maintained.Add((rule.Entity, rule.Level)))
                {
                    throw new InvalidOperationException($"Multiple rules maintain '{rule.Entity.Name}.{rule.Level}'.");
                }
            }
            foreach (ValueTrigger rule in rules.Values)
            {
                if (!maintained.Add((rule.Entity, rule.Target)))
                {
                    throw new InvalidOperationException($"Multiple rules maintain '{rule.Entity.Name}.{rule.Target}'.");
                }
            }
            HashSet<Type> rowTargets = new HashSet<Type>();
            foreach (DistinctRowsRule rule in rules.DistinctRows)
            {
                if (rule.Changes.Count == 0 || !rowTargets.Add(rule.Target) || (rule.Values != null && !rowTargets.Add(rule.Values.Entity)))
                {
                    throw new InvalidOperationException($"Distinct-row rule '{rule.Name}' requires a changed source and exclusive ownership of each target table.");
                }
            }
            if (!registered.IsSupersetOf(rowTargets) || rules.DistinctRows.SelectMany(rule => rule.Changes)
                .Any(change => !registered.Contains(change.Entity) || rowTargets.Contains(change.Entity)))
            {
                throw new InvalidOperationException("Distinct-row sources and targets must be registered, and maintained tables cannot be changed sources.");
            }
            rules.RequiresNestedTriggers = rules.Values.Any(rule => rule.Inputs.Any(input =>
                maintained.Contains((input.Navigation?.PropertyType ?? rule.Entity, input.Property.Name))));
            // Installation and initial population follow the same dependency order as runtime updates.
            List<ValueTrigger> pending = rules.Values.ToList();
            rules.Values.Clear();
            while (pending.Count > 0)
            {
                ValueTrigger? next = pending.Find(rule => !rule.Inputs.Any(input => pending.Any(other =>
                    other.Entity == (input.Navigation?.PropertyType ?? rule.Entity) && other.Target == input.Property.Name)));
                if (next == null)
                {
                    throw new InvalidOperationException("MaintainValue formulas contain a dependency cycle.");
                }
                pending.Remove(next);
                rules.Values.Add(next);
            }
            return rules;
        }

        internal bool RequiresNestedTriggers { get; private set; }
        internal List<CountTrigger> Counts { get; } = new List<CountTrigger>();
        internal List<HierarchyLevelTrigger> HierarchyLevels { get; } = new List<HierarchyLevelTrigger>();
        internal List<ValueTrigger> Values { get; } = new List<ValueTrigger>();
        internal List<DistinctRowsRule> DistinctRows { get; } = new List<DistinctRowsRule>();
    }
}
