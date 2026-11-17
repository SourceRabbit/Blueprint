using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.ComponentModel.DataAnnotations.Schema;
using Blueprint.Localization;
using Blueprint.Database.Entities;
using Blueprint.Database.Entities.Annotations;
using Blueprint.Fundamentals;
using System.Text.Json;

namespace Blueprint.Database.Entities
{
    /// <summary>Generates concrete classes for entity contracts without accessing the database.</summary>
    internal sealed class BlueprintEntityTypes
    {
        private static readonly ConcurrentDictionary<(Type Contract, string Languages), Type> fGeneratedTypes = new();
        private static readonly ModuleBuilder fModule = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Blueprint.Generated"), AssemblyBuilderAccess.Run).DefineDynamicModule("Entities");
        private static readonly object fGenerationLock = new();
        private static long fGeneratedTypeCount;
        private readonly Dictionary<Type, Type> fTypes = new();
        private readonly string fModelKey;
        private readonly IReadOnlyList<BlueprintLanguage> fLanguages;

        /// <summary>Collects entities and their references and generates their implementations.</summary>
        /// <param name="contracts">The initial interfaces to include in the model.</param>
        /// <param name="languages">The translation languages with a unique default.</param>
        /// <exception cref="ArgumentException">An interface is not a valid entity contract.</exception>
        internal BlueprintEntityTypes(IEnumerable<Type> contracts, IReadOnlyList<BlueprintLanguage> languages)
        {
            fLanguages = LanguageColumns.ValidateLanguages(languages).AsReadOnly();
            string languageKey = JsonSerializer.Serialize(fLanguages.OrderBy(language => language.ID));
            Queue<Type> pending = new Queue<Type>(contracts);
            while (pending.TryDequeue(out Type? contract))
            {
                if (fTypes.ContainsKey(contract))
                {
                    continue;
                }

                Validate(contract);
                ValidateTranslations(contract, fLanguages);
                lock (fGenerationLock)
                {
                    fTypes.Add(contract, fGeneratedTypes.GetOrAdd((contract, languageKey), key => Generate(key.Contract, fLanguages)));
                }

                foreach (PropertyInfo property in GetProperties(contract).Where(property => !property.IsDefined(typeof(NotMappedAttribute))))
                {
                    Type? related = GetRelatedContract(property.PropertyType);
                    if (related != null)
                    {
                        pending.Enqueue(related);
                    }
                }
            }
            fModelKey = languageKey + "|" + string.Join("|", fTypes.Keys.Select(type => type.AssemblyQualifiedName).OrderBy(name => name, StringComparer.Ordinal));
        }

        /// <summary>Finds explicitly cached contracts and every contract reached through their AutoInclude relationships.</summary>
        /// <returns>The distinct cached contracts, including transitive automatic relationships.</returns>
        /// <exception cref="InvalidOperationException">An AutoInclude property is not a registered relationship.</exception>
        internal IEnumerable<Type> GetRAMCachedContracts()
        {
            Queue<Type> pending = new Queue<Type>(fTypes.Keys.Where(contract => contract.IsDefined(typeof(RAMCached), false)));
            HashSet<Type> visited = new HashSet<Type>();
            while (pending.TryDequeue(out Type? contract))
            {
                if (!visited.Add(contract))
                {
                    continue;
                }
                yield return contract;
                foreach (PropertyInfo property in GetProperties(contract).Where(property => property.IsDefined(typeof(AutoInclude), true)))
                {
                    Type? related = GetRelatedContract(property.PropertyType);
                    if (related == null || property.IsDefined(typeof(NotMappedAttribute)) || !fTypes.ContainsKey(related))
                    {
                        throw new InvalidOperationException($"AutoInclude property '{contract}.{property.Name}' must be a mapped navigation.");
                    }
                    pending.Enqueue(related);
                }
            }
        }

        /// <summary>The immutable set of languages used for the generated classes.</summary>
        internal IReadOnlyList<BlueprintLanguage> Languages
        {
            get
            {
                return fLanguages;
            }
        }

        /// <summary>The ID of the language with an empty Prefix.</summary>
        internal long DefaultLanguageID
        {
            get
            {
                return fLanguages.Single(language => language.Prefix == string.Empty).ID;
            }
        }

        /// <summary>The mappings from interfaces to generated classes.</summary>
        internal IReadOnlyDictionary<Type, Type> Types
        {
            get
            {
                return fTypes;
            }
        }

        /// <summary>A stable key that lets equivalent entity sets share the same EF model.</summary>
        internal string ModelKey
        {
            get
            {
                return fModelKey;
            }
        }

        /// <summary>Validates that the definition is a public, closed interface implementing BlueprintEntity and carrying the corresponding attribute.</summary>
        /// <param name="contract">The type to validate.</param>
        /// <exception cref="ArgumentException">The type or its members cannot be implemented as an entity.</exception>
        internal static void Validate(Type contract)
        {
            if (!contract.IsInterface || !contract.IsVisible || contract.ContainsGenericParameters || !typeof(BlueprintEntity).IsAssignableFrom(contract) || contract.GetCustomAttribute<EntityAttribute>() == null)
            {
                throw new ArgumentException($"'{contract}' must be a public entity interface marked with [Entity].", nameof(contract));
            }

            Type[] interfaces = contract.GetInterfaces().Append(contract).ToArray();
            if (interfaces.SelectMany(type => type.GetMethods()).Any(method => !method.IsSpecialName || method.IsStatic) || interfaces.SelectMany(type => type.GetEvents()).Any())
            {
                throw new ArgumentException($"Entity '{contract}' can only declare instance properties.", nameof(contract));
            }

            foreach (IGrouping<string, PropertyInfo> group in interfaces.SelectMany(type => type.GetProperties()).GroupBy(property => property.Name))
            {
                if (group.Select(property => property.PropertyType).Distinct().Count() != 1 || group.Any(property => property.GetIndexParameters().Length != 0 || property.GetMethod == null || (property.SetMethod == null && (property.GetMethod.IsAbstract || !property.IsDefined(typeof(NotMappedAttribute))))))
                {
                    throw new ArgumentException($"Entity property '{contract}.{group.Key}' must have compatible getters/setters or an implemented [NotMapped] getter.", nameof(contract));
                }
            }

            foreach (PropertyInfo property in GetProperties(contract).Where(property => property.IsDefined(typeof(FallbackToDefaultLanguage))))
            {
                if (!property.IsDefined(typeof(Multilingual)))
                {
                    throw new ArgumentException($"Fallback property '{contract}.{property.Name}' must also declare [Multilingual].", nameof(contract));
                }
            }

            foreach (PropertyInfo property in GetProperties(contract).Where(property => property.IsDefined(typeof(Multilingual))))
            {
                if (property.PropertyType != typeof(string) || property.IsDefined(typeof(NotMappedAttribute)) || property.Name == nameof(BlueprintEntity.ID))
                {
                    throw new ArgumentException($"Multilingual property '{contract}.{property.Name}' must be a mapped string property.", nameof(contract));
                }
            }
        }

        /// <summary>Rejects conflicts between dynamic translations and other CLR properties or physical columns.</summary>
        /// <param name="contract">The validated interface.</param>
        /// <param name="languages">The languages configured during initialization.</param>
        /// <exception cref="ArgumentException">A duplicate or invalid name is generated.</exception>
        private static void ValidateTranslations(Type contract, IReadOnlyList<BlueprintLanguage> languages)
        {
            PropertyInfo[] properties = GetProperties(contract).ToArray();
            HashSet<string> names = new HashSet<string>(properties.Select(property => property.Name), StringComparer.OrdinalIgnoreCase);
            HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo property in properties.Where(property => !property.IsDefined(typeof(NotMappedAttribute)) && GetRelatedContract(property.PropertyType) == null))
            {
                string columnName = property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name;
                if (!property.IsDefined(typeof(Multilingual)))
                {
                    if (!columns.Add(columnName))
                    {
                        throw new ArgumentException($"Entity '{contract}' produces duplicate column '{columnName}'.", nameof(contract));
                    }
                    continue;
                }
                foreach (BlueprintLanguage language in languages)
                {
                    string translatedColumn = columnName + language.getColumnSuffix();
                    if (!names.Add(LanguageColumns.getPropertyName(property.Name, language.ID)) || !columns.Add(translatedColumn) || translatedColumn.Length > 128 || translatedColumn.Contains('\0'))
                    {
                        throw new ArgumentException($"Multilingual property '{contract}.{property.Name}' produces a duplicate or invalid property/column name.", nameof(contract));
                    }
                }
            }
        }

        /// <summary>Verifies that the ID belongs to this model's languages.</summary>
        /// <param name="languageID">The ID requested by the caller.</param>
        /// <returns>The registered language.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The ID was not configured during initialization.</exception>
        internal BlueprintLanguage getLanguage(long languageID)
        {
            return fLanguages.SingleOrDefault(language => language.ID == languageID) ?? throw new ArgumentOutOfRangeException(nameof(languageID), languageID, "The language is not registered in this Blueprint model.");
        }

        /// <summary>Returns inherited properties, preferring the most specific declaration.</summary>
        /// <param name="contract">The entity interface.</param>
        /// <returns>One property per name.</returns>
        internal static IEnumerable<PropertyInfo> GetProperties(Type contract)
        {
            return new[] { contract }.Concat(contract.GetInterfaces().OrderByDescending(type => type.GetInterfaces().Length)).SelectMany(type => type.GetProperties()).DistinctBy(property => property.Name);
        }

        /// <summary>Identifies references and collections of entity interfaces.</summary>
        /// <param name="propertyType">The property type.</param>
        /// <returns>The related interface, or null for a scalar property.</returns>
        internal static Type? GetRelatedContract(Type propertyType)
        {
            if (typeof(BlueprintEntity).IsAssignableFrom(propertyType))
            {
                return propertyType;
            }

            var collection = propertyType.GetInterfaces().Append(propertyType).FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            var elementType = collection?.GetGenericArguments()[0];
            return elementType != null && typeof(BlueprintEntity).IsAssignableFrom(elementType) ? elementType : null;
        }

        /// <summary>Generates a class with concrete properties and implementations of all inherited accessors.</summary>
        /// <param name="contract">The validated interface.</param>
        /// <param name="languages">The languages of this model.</param>
        /// <returns>The concrete CLR type that EF Core will use.</returns>
        private static Type Generate(Type contract, IReadOnlyList<BlueprintLanguage> languages)
        {
            string typeName = $"{contract.Assembly.GetName().Name}.{contract.FullName}_{++fGeneratedTypeCount}";
            TypeBuilder builder = fModule.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
            builder.AddInterfaceImplementation(contract);
            PropertyInfo[] allProperties = contract.GetInterfaces().Append(contract).SelectMany(type => type.GetProperties()).ToArray();
            long defaultLanguageID = languages.Single(language => language.Prefix == string.Empty).ID;
            (FieldBuilder Field, MethodBuilder Getter, MethodBuilder Setter) languageProperty = DefineProperty(builder, nameof(BlueprintEntity.CurrentLanguage), typeof(long));
            ConstructorBuilder constructor = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            ILGenerator constructorIL = constructor.GetILGenerator();
            constructorIL.Emit(OpCodes.Ldarg_0);
            constructorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            constructorIL.Emit(OpCodes.Ldarg_0);
            constructorIL.Emit(OpCodes.Ldc_I8, defaultLanguageID);
            constructorIL.Emit(OpCodes.Stfld, languageProperty.Field);
            constructorIL.Emit(OpCodes.Ret);

            foreach (PropertyInfo property in GetProperties(contract))
            {
                if (property.SetMethod == null && property.GetMethod?.IsAbstract == false)
                {
                    continue;
                }
                (FieldBuilder Field, MethodBuilder Getter, MethodBuilder Setter) generated = property.Name == nameof(BlueprintEntity.CurrentLanguage) ? languageProperty : DefineProperty(builder, property.Name, property.PropertyType, !property.IsDefined(typeof(Multilingual)));
                if (property.IsDefined(typeof(Multilingual)))
                {
                    ILGenerator getIL = generated.Getter.GetILGenerator();
                    ILGenerator setIL = generated.Setter.GetILGenerator();
                    Dictionary<long, FieldBuilder> translations = languages.ToDictionary(language => language.ID, language => DefineProperty(builder, LanguageColumns.getPropertyName(property.Name, language.ID), typeof(string)).Field);
                    FieldBuilder? fallbackField = property.IsDefined(typeof(FallbackToDefaultLanguage)) ? translations[defaultLanguageID] : null;
                    foreach (BlueprintLanguage language in languages)
                    {
                        EmitTranslationAccessor(getIL, languageProperty.Field, translations[language.ID], language.ID, false, language.ID == defaultLanguageID ? null : fallbackField);
                        EmitTranslationAccessor(setIL, languageProperty.Field, translations[language.ID], language.ID, true);
                    }
                    EmitInvalidLanguage(getIL);
                    EmitInvalidLanguage(setIL);
                }

                foreach (PropertyInfo declaration in allProperties.Where(item => item.Name == property.Name))
                {
                    builder.DefineMethodOverride(generated.Getter, declaration.GetMethod!);
                    builder.DefineMethodOverride(generated.Setter, declaration.SetMethod!);
                }
            }

            return builder.CreateType()!;
        }

        /// <summary>Creates a property with accessors and optional direct storage in a private field.</summary>
        /// <param name="builder">The generated type.</param>
        /// <param name="name">The property name.</param>
        /// <param name="type">The CLR value type.</param>
        /// <param name="emitStorage">Whether the accessors use the field directly.</param>
        /// <returns>The members required to implement the interfaces and multilingual accessors.</returns>
        private static (FieldBuilder Field, MethodBuilder Getter, MethodBuilder Setter) DefineProperty(TypeBuilder builder, string name, Type type, bool emitStorage = true)
        {
            var field = builder.DefineField("f" + name, type, FieldAttributes.Private);
            var property = builder.DefineProperty(name, PropertyAttributes.None, type, null);
            var attributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot;
            var getter = builder.DefineMethod("get_" + name, attributes, type, Type.EmptyTypes);
            var setter = builder.DefineMethod("set_" + name, attributes, null, new[] { type });
            property.SetGetMethod(getter);
            property.SetSetMethod(setter);
            if (emitStorage)
            {
                var getIL = getter.GetILGenerator();
                getIL.Emit(OpCodes.Ldarg_0);
                getIL.Emit(OpCodes.Ldfld, field);
                getIL.Emit(OpCodes.Ret);
                var setIL = setter.GetILGenerator();
                setIL.Emit(OpCodes.Ldarg_0);
                setIL.Emit(OpCodes.Ldarg_1);
                setIL.Emit(OpCodes.Stfld, field);
                setIL.Emit(OpCodes.Ret);
            }
            return (field, getter, setter);
        }

        /// <summary>Routes writes to the selected translation and optionally falls back on reads without changing storage.</summary>
        /// <param name="il">The accessor being generated.</param>
        /// <param name="languageField">The instance's language selection.</param>
        /// <param name="valueField">The stored translation.</param>
        /// <param name="language">The translation language.</param>
        /// <param name="setter">Whether a setter is being generated.</param>
        /// <param name="fallbackField">The default-language field for a fallback-enabled read, or null for direct access.</param>
        private static void EmitTranslationAccessor(ILGenerator il, FieldBuilder languageField, FieldBuilder valueField, long language, bool setter, FieldBuilder? fallbackField = null)
        {
            Label next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, languageField);
            il.Emit(OpCodes.Ldc_I8, language);
            il.Emit(OpCodes.Bne_Un, next);
            il.Emit(OpCodes.Ldarg_0);
            if (setter)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, valueField);
            }
            else
            {
                il.Emit(OpCodes.Ldfld, valueField);
                if (fallbackField != null)
                {
                    Label hasValue = il.DefineLabel();
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Call, typeof(string).GetMethod(nameof(string.IsNullOrEmpty), new[] { typeof(string) })!);
                    il.Emit(OpCodes.Brfalse, hasValue);
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, fallbackField);
                    il.MarkLabel(hasValue);
                }
            }
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }

        /// <summary>Rejects an unknown CurrentLanguage instead of writing to the wrong translation.</summary>
        /// <param name="il">The accessor being completed.</param>
        private static void EmitInvalidLanguage(ILGenerator il)
        {
            il.Emit(OpCodes.Ldstr, nameof(BlueprintEntity.CurrentLanguage));
            il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
        }
    }
}
