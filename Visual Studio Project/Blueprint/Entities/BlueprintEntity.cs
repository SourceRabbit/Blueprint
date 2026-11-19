// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

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

        /// <summary>Serializes scalar values and all stored translations without loading relationships or changing the selected language.</summary>
        /// <returns>A JSON object with ID first and one Translations entry per configured language for multilingual entities.</returns>
        /// <exception cref="InvalidOperationException">The entity contract or its translation storage cannot be represented.</exception>
        public string ToJson()
        {
            EntityJsonFields fields = EntityJsonFields.For(GetType());
            JsonObject json = new JsonObject();
            json.Add(nameof(ID), ID);
            foreach (PropertyInfo property in fields.Scalars.Values)
            {
                if (property.Name != nameof(ID))
                {
                    json.Add(property.Name, JsonSerializer.SerializeToNode(property.GetValue(this)));
                }
            }

            if (fields.Translations.Count > 0)
            {
                JsonArray translations = new JsonArray();
                foreach ((long languageID, Dictionary<string, PropertyInfo> properties) in fields.Translations)
                {
                    JsonObject translation = new JsonObject() { [EntityJsonFields.LANGUAGE_ID] = languageID };
                    foreach ((string name, PropertyInfo storage) in properties)
                    {
                        translation.Add(name, (string?)storage.GetValue(this));
                    }
                    translations.Add(translation);
                }
                json.Add(EntityJsonFields.TRANSLATIONS, translations);
            }

            return json.ToJsonString();
        }
    }
}
