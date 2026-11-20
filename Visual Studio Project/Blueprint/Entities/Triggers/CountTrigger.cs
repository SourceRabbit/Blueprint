// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities.Annotations;
using System.Linq.Expressions;
using System.Reflection;

namespace Blueprint.Entities.Triggers
{
    /// <summary>A counter declaration shared by the entity model and SQL schema synchronization.</summary>
    internal sealed record CountTrigger(string Name, Type Source, Type Target, string ForeignKey, string Counter, LambdaExpression? Include)
    {
        /// <summary>Invokes attributed methods once per entity model and validates counter ownership.</summary>
        /// <param name="contracts">The registered contracts including related entities.</param>
        /// <returns>The validated declarations.</returns>
        /// <exception cref="InvalidOperationException">A declaration has an invalid signature, target or duplicate counter.</exception>
        internal static IReadOnlyList<CountTrigger> Discover(IEnumerable<Type> contracts)
        {
            HashSet<Type> registered = contracts.ToHashSet();
            List<CountTrigger> rules = new List<CountTrigger>();
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
            HashSet<(Type, string)> counters = new HashSet<(Type, string)>();
            foreach (CountTrigger rule in rules)
            {
                if (!registered.Contains(rule.Target) || !counters.Add((rule.Target, rule.Counter)))
                {
                    throw new InvalidOperationException($"Trigger '{rule.Name}' must own a distinct counter on a registered target entity.");
                }
            }
            return rules.AsReadOnly();
        }
    }
}
