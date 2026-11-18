// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.ComponentModel;
using Blueprint.Localization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Database.Queries;
using Blueprint.Fundamentals;

namespace Blueprint.Database
{
    /// <summary>Maps generated subclasses and entity attributes to the EF Core model.</summary>
    public sealed class BlueprintDatabaseContext : DbContext
    {
        private static readonly MethodInfo fQueryMethod = typeof(BlueprintDatabaseContext).GetMethod(nameof(CreateQuery), BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly EntityMaterializer fMaterializer = new();
        private readonly BlueprintEntityTypes fEntityTypes;
        private bool fIsPermanentlyDisposed;

        /// <summary>Connects a set of entities to the provider settings.</summary>
        /// <param name="options">The EF Core settings.</param>
        /// <param name="entityTypes">The types in this model.</param>
        internal BlueprintDatabaseContext(DbContextOptions options, BlueprintEntityTypes entityTypes) : base(options)
        {
            fEntityTypes = entityTypes;
            QueryLanguage = entityTypes.DefaultLanguageID;
        }

        /// <summary>Returns this context to the active database pool without destroying its EF resources.</summary>
        public override void Dispose()
        {
            if (!fIsPermanentlyDisposed)
            {
                BlueprintDatabase.ACTIVE_INSTANCE.ReleaseContext(this);
            }
        }

        /// <summary>Returns this context to the pool at the end of an asynchronous using block.</summary>
        /// <returns>A completed operation after the context is returned.</returns>
        public override ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>Closes EF resources when the database shuts down or replaces its initialized model.</summary>
        internal void DisposePermanently()
        {
            if (!fIsPermanentlyDisposed)
            {
                fIsPermanentlyDisposed = true;
                base.Dispose();
            }
        }

        /// <summary>The immutable type set that defines the context model.</summary>
        internal BlueprintEntityTypes EntityTypes
        {
            get
            {
                return fEntityTypes;
            }
        }

        /// <summary>The language applied to entities materialized by the current query.</summary>
        internal long QueryLanguage { get; set; }

        /// <summary>Connects result materialization to the selected query language.</summary>
        /// <param name="optionsBuilder">The context settings.</param>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.AddInterceptors(fMaterializer);
        }

        /// <summary>Returns a query for the generated type through its public entity class.</summary>
        /// <typeparam name="TEntity">The entity contract.</typeparam>
        /// <returns>An EF Core query without tracking.</returns>
        internal IQueryable<TEntity> Query<TEntity>() where TEntity : BlueprintEntity
        {
            return Query(typeof(TEntity)).Cast<TEntity>();
        }

        /// <summary>Creates the actual DbSet root without loading relationships from the root's source.</summary>
        /// <typeparam name="TEntity">The generated class.</typeparam>
        /// <returns>The query for the concrete CLR type.</returns>
        private IQueryable<TEntity> CreateQuery<TEntity>() where TEntity : class
        {
            return Set<TEntity>().AsNoTrackingWithIdentityResolution();
        }

        /// <summary>Creates an untracked query for a runtime entity contract, leaving AutoInclude loading to its executors.</summary>
        /// <param name="contract">The registered entity class.</param>
        /// <returns>The generated entity query.</returns>
        internal IQueryable Query(Type contract)
        {
            return (IQueryable)fQueryMethod.MakeGenericMethod(fEntityTypes.Types[contract]).Invoke(this, null)!;
        }

        /// <summary>Defines independent entity tables, per-table sequences, indexes, multilingual columns, and relationships.</summary>
        /// <param name="modelBuilder">The EF Core builder.</param>
        /// <exception cref="InvalidOperationException">The table or relationship definitions are incompatible.</exception>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Contracts and their shared bases are not EF inheritance roots or additional tables.
            foreach (Type contract in fEntityTypes.Types.Keys)
            {
                for (Type? type = contract; type != null && type != typeof(object); type = type.BaseType)
                {
                    modelBuilder.Ignore(type);
                }
            }

            HashSet<string> tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((Type contract, Type concreteType) in fEntityTypes.Types)
            {
                EntityAttribute attribute = contract.GetCustomAttribute<EntityAttribute>()!;
                string tableName = attribute.TableName ?? contract.Name;
                if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(attribute.Schema) || !tables.Add(attribute.Schema + "." + tableName))
                {
                    throw new InvalidOperationException($"Entity '{contract}' must have a distinct, nonempty table and schema.");
                }

                EntityTypeBuilder builder = modelBuilder.Entity(concreteType);
                builder.HasBaseType((Type?)null);
                builder.ToTable(tableName, attribute.Schema);
                foreach (PropertyInfo property in global::Blueprint.Entities.BlueprintEntityTypes.GetProperties(contract))
                {
                    if (property.IsDefined(typeof(NotMappedAttribute)))
                    {
                        builder.Ignore(property.Name);
                    }
                    else if (property.IsDefined(typeof(Multilingual)))
                    {
                        builder.Ignore(property.Name);
                        foreach (BlueprintLanguage language in fEntityTypes.Languages)
                        {
                            ConfigureProperty(builder, property, language);
                        }
                    }
                    else if (global::Blueprint.Entities.BlueprintEntityTypes.GetRelatedContract(property.PropertyType) == null)
                    {
                        ConfigureProperty(builder, property);
                    }
                }

                builder.HasKey(nameof(BlueprintEntity.ID));
                modelBuilder.HasSequence<long>(tableName + "_ID", attribute.Schema).StartsAt(1).IncrementsBy(1);
                string sequenceName = this.GetService<ISqlGenerationHelper>().DelimitIdentifier(tableName + "_ID", attribute.Schema);
                builder.Property<long>(nameof(BlueprintEntity.ID)).HasDefaultValueSql("NEXT VALUE FOR " + sequenceName).ValueGeneratedOnAdd();

                foreach (EntityIndex index in contract.GetCustomAttributes<EntityIndex>())
                {
                    if (index.PropertyNames.Any(name => builder.Metadata.FindProperty(name) == null))
                    {
                        throw new InvalidOperationException($"Index '{index.Name}' on entity '{contract}' must reference mapped scalar properties.");
                    }
                    builder.HasIndex(index.PropertyNames.ToArray()).HasDatabaseName(index.Name).IsUnique(index.IsUnique);
                }
            }

            foreach ((Type contract, Type concreteType) in fEntityTypes.Types)
            {
                foreach (PropertyInfo property in global::Blueprint.Entities.BlueprintEntityTypes.GetProperties(contract).Where(property => typeof(BlueprintEntity).IsAssignableFrom(property.PropertyType) && !property.IsDefined(typeof(NotMappedAttribute))))
                {
                    ConfigureReference(modelBuilder, concreteType, contract, property);
                }
            }

            foreach ((Type contract, Type concreteType) in fEntityTypes.Types)
            {
                foreach (PropertyInfo property in global::Blueprint.Entities.BlueprintEntityTypes.GetProperties(contract).Where(property => !property.IsDefined(typeof(NotMappedAttribute))))
                {
                    if (global::Blueprint.Entities.BlueprintEntityTypes.GetRelatedContract(property.PropertyType) != null && modelBuilder.Entity(concreteType).Metadata.FindNavigation(property.Name) == null)
                    {
                        throw new InvalidOperationException($"Navigation '{contract}.{property.Name}' requires an inverse reference with an unambiguous foreign key.");
                    }
                }
            }
        }

        /// <summary>Transfers scalar property constraints from the entity class to EF Core.</summary>
        /// <param name="builder">The generated class builder.</param>
        /// <param name="property">The original property declaration.</param>
        /// <param name="language">The translation language, or null for a shared property.</param>
        private static void ConfigureProperty(EntityTypeBuilder builder, PropertyInfo property, BlueprintLanguage? language = null)
        {
            PropertyBuilder propertyBuilder = builder.Property(property.PropertyType, language == null ? property.Name : LanguageColumns.getPropertyName(property.Name, language.ID));
            ColumnAttribute? column = property.GetCustomAttribute<ColumnAttribute>();
            if (column?.Name != null || language != null)
            {
                propertyBuilder.HasColumnName((column?.Name ?? property.Name) + language?.getColumnSuffix());
            }
            if (column?.TypeName != null)
            {
                propertyBuilder.HasColumnType(column.TypeName);
            }
            int? maxLength = property.GetCustomAttribute<MaxLengthAttribute>()?.Length ?? property.GetCustomAttribute<StringLengthAttribute>()?.MaximumLength;
            if (maxLength > 0)
            {
                propertyBuilder.HasMaxLength(maxLength.Value);
            }
            PrecisionAttribute? precision = property.GetCustomAttribute<PrecisionAttribute>();
            if (precision != null)
            {
                propertyBuilder.HasPrecision(precision.Precision, precision.Scale ?? 0);
            }
            if (language != null)
            {
                propertyBuilder.IsRequired(property.IsDefined(typeof(RequiredAttribute)));
            }
            else if (property.IsDefined(typeof(RequiredAttribute)) || new NullabilityInfoContext().Create(property).ReadState == NullabilityState.NotNull)
            {
                propertyBuilder.IsRequired();
            }
            if (property.IsDefined(typeof(ConcurrencyCheckAttribute)))
            {
                propertyBuilder.IsConcurrencyToken();
            }
            if (property.IsDefined(typeof(TimestampAttribute)))
            {
                propertyBuilder.IsRowVersion();
            }
            DefaultValueAttribute? defaultValue = property.GetCustomAttribute<DefaultValueAttribute>();
            if (defaultValue != null)
            {
                propertyBuilder.HasDefaultValue(defaultValue.Value);
            }
            if (property.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly == true)
            {
                propertyBuilder.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            }
        }

        /// <summary>Maps a reference and optional inverse collection using the unique ID.</summary>
        /// <param name="modelBuilder">The model builder.</param>
        /// <param name="concreteType">The generated class of the dependent entity.</param>
        /// <param name="contract">The dependent entity class.</param>
        /// <param name="property">The reference property.</param>
        /// <exception cref="InvalidOperationException">The relationship does not specify existing columns.</exception>
        private void ConfigureReference(ModelBuilder modelBuilder, Type concreteType, Type contract, PropertyInfo property)
        {
            var related = property.PropertyType;
            var properties = global::Blueprint.Entities.BlueprintEntityTypes.GetProperties(contract).ToArray();
            var foreignKey = property.GetCustomAttribute<ForeignKeyAttribute>()?.Name;
            var keys = foreignKey?.Split(',', StringSplitOptions.TrimEntries) ?? new[] { property.Name + "ID" };
            if (keys.Any(key => properties.All(item => item.Name != key)))
            {
                throw new InvalidOperationException($"Navigation '{contract}.{property.Name}' requires foreign key properties '{string.Join(", ", keys)}'.");
            }

            var inverseName = property.GetCustomAttribute<InversePropertyAttribute>()?.Property;
            var candidates = global::Blueprint.Entities.BlueprintEntityTypes.GetProperties(related).Where(item => item.PropertyType != contract && global::Blueprint.Entities.BlueprintEntityTypes.GetRelatedContract(item.PropertyType) == contract && !item.IsDefined(typeof(NotMappedAttribute))).ToArray();
            if (inverseName == null)
            {
                var matching = candidates.Where(item => item.GetCustomAttribute<InversePropertyAttribute>()?.Property == property.Name).ToArray();
                inverseName = matching.Length == 1 ? matching[0].Name : candidates.Length == 1 && candidates[0].GetCustomAttribute<InversePropertyAttribute>() == null ? candidates[0].Name : null;
            }

            modelBuilder.Entity(concreteType).HasOne(fEntityTypes.Types[related], property.Name).WithMany(inverseName).HasForeignKey(keys).OnDelete(DeleteBehavior.Restrict);
        }
    }

    /// <summary>Applies the query language to entities in relationships or projections as well.</summary>
    internal sealed class EntityMaterializer : IMaterializationInterceptor
    {
        /// <summary>Sets the language after stored properties have been read.</summary>
        /// <param name="materializationData">The context executing the query.</param>
        /// <param name="entity">The materialized instance.</param>
        /// <returns>The same instance with the selected language.</returns>
        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is BlueprintEntity value && materializationData.Context is BlueprintDatabaseContext context)
            {
                value.CurrentLanguage = context.QueryLanguage;
            }
            return entity;
        }
    }

    /// <summary>Isolates the EF Core cache for each immutable entity set.</summary>
    internal sealed class EntityModelCacheKey : IModelCacheKeyFactory
    {
        /// <summary>Returns a key that distinguishes dynamic models and design-time metadata.</summary>
        /// <param name="context">The context whose model is being built.</param>
        /// <param name="designTime">Whether full schema-creation metadata is required.</param>
        /// <returns>The model cache key.</returns>
        public object Create(DbContext context, bool designTime)
        {
            return (context.GetType(), ((BlueprintDatabaseContext)context).EntityTypes.ModelKey, designTime);
        }
    }
}
