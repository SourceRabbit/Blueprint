// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database;
using Blueprint.Entities.Annotations;
using Blueprint.Localization;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;

namespace Blueprint.Entities
{
    /// <summary>Creates registered Blueprint entities from their scalar JSON representation.</summary>
    public static class BlueprintEntityJson
    {
        extension<TEntity>(TEntity) where TEntity : BlueprintEntity
        {
            /// <summary>Creates an unsaved entity and restores its scalar values and per-language translations.</summary>
            /// <param name="json">A JSON object using the entity's public property names.</param>
            /// <returns>A new entity containing the supplied values in memory.</returns>
            /// <exception cref="ArgumentNullException">The JSON string is null.</exception>
            /// <exception cref="JsonException">The JSON object, property name, value, or language is invalid.</exception>
            /// <exception cref="InvalidOperationException">No initialized database is active or the entity type is unregistered.</exception>
            public static TEntity FromJson(string json)
            {
                ArgumentNullException.ThrowIfNull(json);
                JsonElement source = JsonElement.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
                if (source.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("Expected a JSON object.");
                }

                TEntity entity = BlueprintDatabase.ACTIVE_INSTANCE.Create<TEntity>();
                EntityJsonFields fields = EntityJsonFields.For(entity.GetType());
                foreach (JsonProperty field in source.EnumerateObject())
                {
                    if (field.NameEquals(EntityJsonFields.TRANSLATIONS) && fields.Translations.Count > 0)
                    {
                        ReadTranslations(entity, field.Value, fields);
                        continue;
                    }
                    if (!fields.Scalars.TryGetValue(field.Name, out PropertyInfo? property))
                    {
                        throw new JsonException($"Unexpected property '{field.Name}' for {typeof(TEntity).Name}.");
                    }

                    object? value = field.Value.Deserialize(property.PropertyType);
                    property.SetValue(entity, value);
                }

                return entity;
            }
        }

        /// <summary>Writes each supplied translation to its stored language slot, preserving the entity's default language.</summary>
        /// <param name="entity">The newly created entity.</param>
        /// <param name="translations">The JSON array of translations.</param>
        /// <param name="fields">The scalar and translation metadata for the concrete type.</param>
        /// <exception cref="JsonException">A language is missing, unknown, or repeated, or a translation field is invalid.</exception>
        private static void ReadTranslations(BlueprintEntity entity, JsonElement translations, EntityJsonFields fields)
        {
            if (translations.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Translations must be an array.");
            }

            HashSet<long> seen = new HashSet<long>();
            foreach (JsonElement entry in translations.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(EntityJsonFields.LANGUAGE_ID, out JsonElement language) ||
                    language.ValueKind != JsonValueKind.Number || !language.TryGetInt64(out long languageID))
                {
                    throw new JsonException("Each translation must have an integer LanguageID.");
                }
                if (!fields.Translations.TryGetValue(languageID, out Dictionary<string, PropertyInfo>? properties) || !seen.Add(languageID))
                {
                    throw new JsonException($"Unknown or duplicate translation LanguageID {languageID}.");
                }

                foreach (JsonProperty field in entry.EnumerateObject())
                {
                    if (field.NameEquals(EntityJsonFields.LANGUAGE_ID))
                    {
                        continue;
                    }
                    if (!properties.TryGetValue(field.Name, out PropertyInfo? storage))
                    {
                        throw new JsonException($"Unexpected translation property '{field.Name}'.");
                    }
                    storage.SetValue(entity, field.Value.Deserialize<string?>());
                }
            }
        }
    }

    /// <summary>Maps JSON names to contract properties and to the raw translation storage of each generated type.</summary>
    internal sealed class EntityJsonFields
    {
        internal const string TRANSLATIONS = "Translations";
        internal const string LANGUAGE_ID = "LanguageID";
        private static readonly ConcurrentDictionary<Type, EntityJsonFields> fTypes = new ConcurrentDictionary<Type, EntityJsonFields>();

        /// <summary>Builds the field maps shared by the JSON reader and writer.</summary>
        /// <param name="type">The runtime entity type.</param>
        /// <exception cref="InvalidOperationException">The contract is missing or cannot be represented in the multilingual JSON format.</exception>
        private EntityJsonFields(Type type)
        {
            Type contract = type;
            while (!contract.IsDefined(typeof(EntityAttribute), false))
            {
                contract = contract.BaseType ?? throw new InvalidOperationException("Missing [Entity] contract.");
            }

            PropertyInfo[] mapped = BlueprintEntityTypes.GetProperties(contract)
                .Where(property => !property.IsDefined(typeof(NotMappedAttribute)) && BlueprintEntityTypes.GetRelatedContract(property.PropertyType) == null).ToArray();
            Scalars = mapped.Where(property => !property.IsDefined(typeof(Multilingual))).ToDictionary(property => property.Name, property => type.GetProperty(property.Name)!, StringComparer.Ordinal);
            PropertyInfo[] translated = mapped.Where(property => property.IsDefined(typeof(Multilingual))).ToArray();
            if (translated.Length > 0)
            {
                if (Scalars.ContainsKey(TRANSLATIONS) || translated.Any(property => property.Name == LANGUAGE_ID))
                {
                    throw new InvalidOperationException($"'{contract}' has a property that conflicts with the multilingual JSON format.");
                }
                foreach (long languageID in BlueprintEntityTypes.GetLanguageIDs(type))
                {
                    Translations.Add(languageID, translated.ToDictionary(property => property.Name,
                        property => type.GetProperty(LanguageColumns.getPropertyName(property.Name, languageID))!, StringComparer.Ordinal));
                }
            }
        }

        /// <summary>Gets the field mappings for the entity's generated model without accessing a database.</summary>
        /// <param name="type">The runtime entity type.</param>
        /// <returns>The cached mappings for this type.</returns>
        internal static EntityJsonFields For(Type type)
        {
            return fTypes.GetOrAdd(type, static key => new EntityJsonFields(key));
        }

        internal Dictionary<string, PropertyInfo> Scalars { get; }
        internal Dictionary<long, Dictionary<string, PropertyInfo>> Translations { get; } = new Dictionary<long, Dictionary<string, PropertyInfo>>();
    }
}
