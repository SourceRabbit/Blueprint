// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Entities.Annotations
{
    /// <summary>Declares an index on stored properties of a BlueprintEntity class.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class EntityIndex : Attribute
    {
        private readonly string fName;
        private readonly IReadOnlyList<string> fPropertyNames;

        /// <summary>Defines an index on one or more stored properties.</summary>
        /// <param name="name">The index name in the database.</param>
        /// <param name="propertyNames">The names of the participating properties.</param>
        /// <exception cref="ArgumentNullException">No property list was provided.</exception>
        /// <exception cref="ArgumentException">The name or properties are empty or duplicated.</exception>
        public EntityIndex(string name, params string[] propertyNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(propertyNames);
            if (propertyNames.Length == 0 || propertyNames.Any(string.IsNullOrWhiteSpace) || propertyNames.Distinct(StringComparer.Ordinal).Count() != propertyNames.Length)
            {
                throw new ArgumentException("An entity index requires distinct, nonempty property names.", nameof(propertyNames));
            }

            fName = name;
            fPropertyNames = Array.AsReadOnly(propertyNames.ToArray());
        }

        /// <summary>The index name in the database.</summary>
        public string Name
        {
            get
            {
                return fName;
            }
        }

        /// <summary>The indexed properties in declaration order.</summary>
        public IReadOnlyList<string> PropertyNames
        {
            get
            {
                return fPropertyNames;
            }
        }

        /// <summary>Whether the combination of values must be unique.</summary>
        public bool IsUnique { get; set; }
    }
}
