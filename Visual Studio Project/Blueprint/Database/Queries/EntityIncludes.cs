// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Database.Queries.QueryExecutors;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Reflection;

namespace Blueprint.Database.Queries
{
    /// <summary>Loads AutoInclude graphs through each target entity's executor, preserving independent objects and cyclic references.</summary>
    internal sealed class EntityIncludes
    {
        private readonly BlueprintDatabaseContext fContext;
        private readonly long fLanguage;
        private readonly Dictionary<(Type, long), BlueprintEntity> fEntities = new Dictionary<(Type, long), BlueprintEntity>();

        /// <summary>Creates one identity scope for a materialized result graph.</summary>
        /// <param name="context">The context reserved for the current operation.</param>
        /// <param name="language">The language shared by the graph.</param>
        private EntityIncludes(BlueprintDatabaseContext context, long language)
        {
            fContext = context;
            fLanguage = language;
        }

        /// <summary>Loads declared relationships after root materialization, selecting DB or RAM for every related contract.</summary>
        /// <param name="context">The context reserved for the current operation.</param>
        /// <param name="entities">Independent root entities.</param>
        /// <param name="language">The language applied to related entities.</param>
        /// <exception cref="Exception">Relationship mapping, synchronization, or SQL execution fails.</exception>
        internal static void Load(BlueprintDatabaseContext context, IEnumerable<BlueprintEntity> entities, long language)
        {
            EntityIncludes loader = new EntityIncludes(context, language);
            List<BlueprintEntity> roots = entities.ToList();
            foreach (BlueprintEntity entity in roots)
            {
                loader.fEntities.TryAdd((entity.GetType(), entity.ID), entity);
            }
            loader.LoadEntities(roots, new HashSet<Type>());
        }

        /// <summary>Batches relationship keys by target and stops expansion when a type repeats in an include path.</summary>
        /// <param name="entities">The independent entities at this level.</param>
        /// <param name="ancestors">Types already expanded along the current path.</param>
        /// <exception cref="Exception">A relationship cannot be loaded.</exception>
        private void LoadEntities(IReadOnlyList<BlueprintEntity> entities, HashSet<Type> ancestors)
        {
            foreach (IGrouping<Type, BlueprintEntity> group in entities.GroupBy(entity => entity.GetType()))
            {
                if (ancestors.Contains(group.Key))
                {
                    continue;
                }
                HashSet<Type> path = new HashSet<Type>(ancestors) { group.Key };
                Type contract = fContext.EntityTypes.Types.Single(pair => pair.Value == group.Key).Key;
                IEntityType model = fContext.Model.FindEntityType(group.Key)!;
                foreach (PropertyInfo property in BlueprintEntityTypes.GetProperties(contract).Where(property => property.IsDefined(typeof(AutoInclude), true)))
                {
                    INavigation navigation = model.FindNavigation(property.Name) ?? throw new InvalidOperationException($"AutoInclude property '{contract}.{property.Name}' must be a mapped navigation.");
                    IProperty foreignKey = navigation.ForeignKey.Properties.Single();
                    IProperty primaryKey = navigation.ForeignKey.PrincipalKey.Properties.Single();
                    IProperty parentKey = navigation.IsOnDependent ? foreignKey : primaryKey;
                    IProperty targetKey = navigation.IsOnDependent ? primaryKey : foreignKey;
                    long[] ids = group.Select(entity => parentKey.PropertyInfo!.GetValue(entity)).OfType<long>().Distinct().ToArray();
                    Type targetContract = fContext.EntityTypes.Types.Single(pair => pair.Value == navigation.TargetEntityType.ClrType).Key;
                    QueryExecutor executor = QueryExecutor.Create(targetContract, fContext.Query(targetContract).Provider, fLanguage);
                    List<BlueprintEntity> related = new List<BlueprintEntity>();
                    if (ids.Length > 0)
                    {
                        foreach (BlueprintEntity row in executor.ReadRelated(fContext, targetKey, ids))
                        {
                            (Type, long) identity = (row.GetType(), row.ID);
                            if (!fEntities.TryGetValue(identity, out BlueprintEntity? instance))
                            {
                                instance = row;
                                fEntities.Add(identity, row);
                            }
                            related.Add(instance);
                        }
                    }
                    LoadEntities(related, path);
                    ILookup<object?, BlueprintEntity> matches = related.ToLookup(entity => targetKey.PropertyInfo!.GetValue(entity));
                    foreach (BlueprintEntity entity in group)
                    {
                        IEnumerable<BlueprintEntity> values = matches[parentKey.PropertyInfo!.GetValue(entity)];
                        if (navigation.IsCollection)
                        {
                            IClrCollectionAccessor accessor = navigation.GetCollectionAccessor()!;
                            property.SetValue(entity, accessor.Create());
                            foreach (BlueprintEntity value in values)
                            {
                                accessor.Add(entity, value, true);
                                FixInverse(navigation, entity, value);
                            }
                        }
                        else
                        {
                            BlueprintEntity? value = values.SingleOrDefault();
                            property.SetValue(entity, value);
                            if (value != null)
                            {
                                FixInverse(navigation, entity, value);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Connects inverse references and collections only on independent materialized objects.</summary>
        /// <param name="navigation">The loaded navigation.</param>
        /// <param name="entity">The source entity.</param>
        /// <param name="related">The selected related entity.</param>
        private static void FixInverse(INavigation navigation, BlueprintEntity entity, BlueprintEntity related)
        {
            INavigation? inverse = navigation.Inverse;
            if (inverse?.IsCollection == true)
            {
                inverse.GetCollectionAccessor()!.Add(related, entity, true);
            }
            else if (inverse != null)
            {
                inverse.PropertyInfo!.SetValue(related, entity);
            }
        }
    }
}
