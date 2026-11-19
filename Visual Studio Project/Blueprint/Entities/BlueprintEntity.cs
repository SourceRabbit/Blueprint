// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities.Annotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blueprint.Entities
{
    /// <summary>The shared identity of every entity, with a unique increasing number per table.</summary>
    public abstract class BlueprintEntity
    {
        /// <summary>The unique long primary key (bigint in SQL Server), populated by the database on insertion.</summary>
        public long ID { get; set; }

        /// <summary>The BlueprintLanguage.ID used to read and write Multilingual properties. Defaults to the language with an empty Prefix. Not stored as a column.</summary>
        [NotMapped]
        public long CurrentLanguage { get; set; }


        /// <summary>Serializes the entity's current scalar values without loading relationships.</summary>
        /// <returns>A JSON string with ID first, followed by CurrentLanguage for multilingual entities and the remaining mapped scalar properties.</returns>
        /// <exception cref="InvalidOperationException">The entity has no contract marked with [Entity].</exception>
        public string JSON()
        {
            Type contract = GetType();
            while (!contract.IsDefined(typeof(EntityAttribute), false))
            {
                contract = contract.BaseType ?? throw new InvalidOperationException("Missing [Entity] contract.");
            }

            PropertyInfo[] properties = BlueprintEntityTypes.GetProperties(contract).ToArray();
            JsonObject json = new JsonObject();
            json.Add(nameof(ID), ID);
            if (properties.Any(property => property.IsDefined(typeof(Multilingual))))
            {
                json.Add(nameof(CurrentLanguage), CurrentLanguage);
            }

            foreach (PropertyInfo property in properties)
            {
                if (property.Name == nameof(ID) || property.IsDefined(typeof(NotMappedAttribute)) ||
                    BlueprintEntityTypes.GetRelatedContract(property.PropertyType) != null)
                {
                    continue;
                }

                json.Add(property.Name, JsonSerializer.SerializeToNode(property.GetValue(this)));
            }

            return json.ToJsonString();
        }
    }
}
