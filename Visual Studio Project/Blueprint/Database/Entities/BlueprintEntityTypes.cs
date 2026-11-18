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
        /// <param name="contracts">The initial entity classes to include in the model.</param>
        /// <param name="languages">The translation languages with a unique default.</param>
        /// <exception cref="ArgumentException">A class is not a valid entity contract.</exception>
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

        /// <summary>Selects RAM entity classes without changing the source of related DB entities.</summary>
        /// <returns>The registered classes derived from BlueprintEntityRAM.</returns>
        internal IEnumerable<Type> GetRAMCachedContracts()
        {
            return fTypes.Keys.Where(contract => typeof(BlueprintEntityRAM).IsAssignableFrom(contract));
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

        /// <summary>The mappings from entity classes to generated subclasses.</summary>
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

        /// <summary>Validates an inheritable entity class and the accessors needed by its generated implementation.</summary>
        /// <param name="contract">The type to validate.</param>
        /// <exception cref="ArgumentException">The type or its members cannot be implemented as an entity.</exception>
        internal static void Validate(Type contract)
        {
            if (!contract.IsClass || contract.IsSealed || !contract.IsVisible || contract.ContainsGenericParameters || (!typeof(BlueprintEntityDB).IsAssignableFrom(contract) && !typeof(BlueprintEntityRAM).IsAssignableFrom(contract)) || contract.GetCustomAttribute<EntityAttribute>(false) == null)
            {
                throw new ArgumentException($"'{contract}' must be a public, non-sealed class derived from BlueprintEntityDB or BlueprintEntityRAM and marked with [Entity].", nameof(contract));
            }

            ConstructorInfo? constructor = contract.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (constructor == null || (!constructor.IsPublic && !constructor.IsFamily && !constructor.IsFamilyOrAssembly))
            {
                throw new ArgumentException($"Entity '{contract}' must have a public or protected parameterless constructor.", nameof(contract));
            }

            PropertyInfo[] properties = GetProperties(contract).ToArray();
            HashSet<MethodInfo> implementedAccessors = new HashSet<MethodInfo>();
            foreach (PropertyInfo property in properties)
            {
                if (property.IsDefined(typeof(AutoInclude), true) && (property.IsDefined(typeof(NotMappedAttribute)) || GetRelatedContract(property.PropertyType) == null))
                {
                    throw new ArgumentException($"AutoInclude property '{contract}.{property.Name}' must be a mapped navigation.", nameof(contract));
                }
                MethodInfo? getter = property.GetMethod;
                MethodInfo? setter = property.SetMethod;
                bool generated = getter?.IsAbstract == true || setter?.IsAbstract == true || property.IsDefined(typeof(Multilingual));
                bool invalidGetter = getter == null || !getter.IsPublic || getter.IsStatic;
                bool invalidSetter = setter == null ? getter?.IsAbstract == true || !property.IsDefined(typeof(NotMappedAttribute)) : !setter.IsPublic || setter.IsStatic;
                bool invalidOverride = generated && (getter?.IsVirtual != true || getter.IsFinal || setter?.IsVirtual != true || setter.IsFinal);
                if (property.GetIndexParameters().Length != 0 || invalidGetter || invalidSetter || invalidOverride)
                {
                    throw new ArgumentException($"Entity property '{contract}.{property.Name}' must have public getters/setters or an implemented [NotMapped] getter. Multilingual and abstract properties require overridable getters and setters.", nameof(contract));
                }
                implementedAccessors.Add(getter!);
                if (setter != null)
                {
                    implementedAccessors.Add(setter);
                }
            }

            if (contract.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(method => method.IsAbstract && !implementedAccessors.Contains(method)))
            {
                throw new ArgumentException($"Entity '{contract}' cannot declare abstract methods or events outside its public properties.", nameof(contract));
            }

            foreach (Type type in GetHierarchy(contract))
            {
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    PropertyInfo selected = properties.Single(item => item.Name == property.Name);
                    if (selected.PropertyType != property.PropertyType || selected.GetMethod?.GetBaseDefinition() != property.GetMethod?.GetBaseDefinition())
                    {
                        throw new ArgumentException($"Entity '{contract}' cannot hide inherited property '{property.Name}'.", nameof(contract));
                    }
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
        /// <param name="contract">The validated entity class.</param>
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
        /// <param name="contract">The entity class.</param>
        /// <returns>One property per name.</returns>
        internal static IEnumerable<PropertyInfo> GetProperties(Type contract)
        {
            return GetHierarchy(contract).SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)).DistinctBy(property => property.Name);
        }

        /// <summary>Enumerates the entity's class hierarchy from the most specific declaration to its shared base.</summary>
        /// <param name="contract">The entity class.</param>
        /// <returns>The entity class and its base classes, excluding System.Object.</returns>
        private static IEnumerable<Type> GetHierarchy(Type contract)
        {
            for (Type? type = contract; type != null && type != typeof(object); type = type.BaseType)
            {
                yield return type;
            }
        }

        /// <summary>Identifies references and collections of entity classes.</summary>
        /// <param name="propertyType">The property type.</param>
        /// <returns>The related entity class, or null for a scalar property.</returns>
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

        /// <summary>Generates a subclass implementing abstract properties and language-aware overrides while retaining ordinary class behavior.</summary>
        /// <param name="contract">The validated entity class.</param>
        /// <param name="languages">The languages of this model.</param>
        /// <returns>The concrete CLR type that EF Core will use.</returns>
        private static Type Generate(Type contract, IReadOnlyList<BlueprintLanguage> languages)
        {
            string typeName = $"{contract.Assembly.GetName().Name}.{contract.FullName}_{++fGeneratedTypeCount}";
            TypeBuilder builder = fModule.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, contract);
            long defaultLanguageID = languages.Single(language => language.Prefix == string.Empty).ID;
            PropertyInfo languageProperty = typeof(BlueprintEntity).GetProperty(nameof(BlueprintEntity.CurrentLanguage))!;
            ConstructorBuilder constructor = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            ILGenerator constructorIL = constructor.GetILGenerator();
            // Initialize the language before a base constructor can assign a virtual multilingual property.
            constructorIL.Emit(OpCodes.Ldarg_0);
            constructorIL.Emit(OpCodes.Ldc_I8, defaultLanguageID);
            constructorIL.Emit(OpCodes.Call, languageProperty.SetMethod!);
            constructorIL.Emit(OpCodes.Ldarg_0);
            constructorIL.Emit(OpCodes.Call, contract.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)!);
            constructorIL.Emit(OpCodes.Ret);

            foreach (PropertyInfo property in GetProperties(contract))
            {
                if (property.GetMethod?.IsAbstract != true && property.SetMethod?.IsAbstract != true && !property.IsDefined(typeof(Multilingual)))
                {
                    continue;
                }
                (FieldBuilder Field, MethodBuilder Getter, MethodBuilder Setter) generated = DefineProperty(builder, property.Name, property.PropertyType, !property.IsDefined(typeof(Multilingual)));
                if (property.IsDefined(typeof(Multilingual)))
                {
                    ILGenerator getIL = generated.Getter.GetILGenerator();
                    ILGenerator setIL = generated.Setter.GetILGenerator();
                    Dictionary<long, FieldBuilder> translations = languages.ToDictionary(language => language.ID, language => DefineProperty(builder, LanguageColumns.getPropertyName(property.Name, language.ID), typeof(string)).Field);
                    FieldBuilder? fallbackField = property.IsDefined(typeof(FallbackToDefaultLanguage)) ? translations[defaultLanguageID] : null;
                    foreach (BlueprintLanguage language in languages)
                    {
                        EmitTranslationAccessor(getIL, languageProperty.GetMethod!, translations[language.ID], language.ID, false, language.ID == defaultLanguageID ? null : fallbackField);
                        EmitTranslationAccessor(setIL, languageProperty.GetMethod!, translations[language.ID], language.ID, true);
                    }
                    EmitInvalidLanguage(getIL);
                    EmitInvalidLanguage(setIL);
                }

                builder.DefineMethodOverride(generated.Getter, property.GetMethod!);
                builder.DefineMethodOverride(generated.Setter, property.SetMethod!);
            }

            return builder.CreateType()!;
        }

        /// <summary>Creates a property with accessors and optional direct storage in a private field.</summary>
        /// <param name="builder">The generated type.</param>
        /// <param name="name">The property name.</param>
        /// <param name="type">The CLR value type.</param>
        /// <param name="emitStorage">Whether the accessors use the field directly.</param>
        /// <returns>The members required to implement abstract properties and multilingual accessors.</returns>
        private static (FieldBuilder Field, MethodBuilder Getter, MethodBuilder Setter) DefineProperty(TypeBuilder builder, string name, Type type, bool emitStorage = true)
        {
            FieldBuilder field = builder.DefineField("f" + name, type, FieldAttributes.Private);
            PropertyBuilder property = builder.DefineProperty(name, PropertyAttributes.None, type, null);
            MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.SpecialName;
            MethodBuilder getter = builder.DefineMethod("get_" + name, attributes, type, Type.EmptyTypes);
            MethodBuilder setter = builder.DefineMethod("set_" + name, attributes, null, new[] { type });
            property.SetGetMethod(getter);
            property.SetSetMethod(setter);
            if (emitStorage)
            {
                ILGenerator getIL = getter.GetILGenerator();
                getIL.Emit(OpCodes.Ldarg_0);
                getIL.Emit(OpCodes.Ldfld, field);
                getIL.Emit(OpCodes.Ret);
                ILGenerator setIL = setter.GetILGenerator();
                setIL.Emit(OpCodes.Ldarg_0);
                setIL.Emit(OpCodes.Ldarg_1);
                setIL.Emit(OpCodes.Stfld, field);
                setIL.Emit(OpCodes.Ret);
            }
            return (field, getter, setter);
        }

        /// <summary>Routes writes to the selected translation and optionally falls back on reads without changing storage.</summary>
        /// <param name="il">The accessor being generated.</param>
        /// <param name="languageGetter">The inherited accessor for the instance's language selection.</param>
        /// <param name="valueField">The stored translation.</param>
        /// <param name="language">The translation language.</param>
        /// <param name="setter">Whether a setter is being generated.</param>
        /// <param name="fallbackField">The default-language field for a fallback-enabled read, or null for direct access.</param>
        private static void EmitTranslationAccessor(ILGenerator il, MethodInfo languageGetter, FieldBuilder valueField, long language, bool setter, FieldBuilder? fallbackField = null)
        {
            Label next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, languageGetter);
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
