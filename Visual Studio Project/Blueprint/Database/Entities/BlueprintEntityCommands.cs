using Blueprint.Database.Contexts;
using Blueprint.Database.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace Blueprint.Database.Entities
{
    /// <summary>Persists entity changes immediately through SQL and invalidates cached snapshots.</summary>
    /// <typeparam name="TEntity">The registered entity interface.</typeparam>
    internal sealed class BlueprintEntityCommands<TEntity> where TEntity : class, BlueprintEntity
    {
        private readonly BlueprintDatabase fDatabase;
        private readonly long fLanguage;

        /// <summary>Binds write operations to a validated entity type and language.</summary>
        /// <param name="database">The database that owns the context and cache.</param>
        /// <param name="language">The language used by predicate deletes.</param>
        internal BlueprintEntityCommands(BlueprintDatabase database, long language)
        {
            fDatabase = database;
            fLanguage = language;
        }

        /// <summary>Inserts an entity and populates its ID when it is zero.</summary>
        /// <param name="entity">The entity to persist.</param>
        /// <returns>The number of rows inserted.</returns>
        public int Insert(TEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return Insert(new[] { entity });
        }

        /// <summary>Inserts entities in one SaveChanges call with new IDs from database sequences.</summary>
        /// <param name="entities">The entities to persist.</param>
        /// <returns>The number of inserted rows.</returns>
        /// <exception cref="DbUpdateException">Insertion fails or a key constraint is violated.</exception>
        /// <exception cref="ArgumentException">A new entity already has an ID assigned.</exception>
        public int Insert(IEnumerable<TEntity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            return fDatabase.ExecuteOperation(context =>
            {
                List<TEntity> items = ValidateEntities(context, entities);
                if (items.Any(entity => entity.ID != 0))
                {
                    throw new ArgumentException("New entities must have ID = 0. The database generates their IDs during insertion.", nameof(entities));
                }
                try
                {
                    foreach (TEntity entity in items)
                    {
                        context.Entry(entity).State = EntityState.Added;
                    }
                    return context.SaveChanges();
                }
                finally
                {
                    context.ChangeTracker.Clear();
                    fDatabase.InvalidateCache();
                }
            });
        }

        /// <summary>Updates a row using its full primary key.</summary>
        /// <param name="entity">The entity with new values.</param>
        /// <returns>One if the row was found; otherwise zero.</returns>
        public int Update(TEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return Update(new[] { entity });
        }

        /// <summary>Updates existing rows and all their translations using the unique ID.</summary>
        /// <param name="entities">The entities with new values.</param>
        /// <returns>The number of rows updated.</returns>
        /// <exception cref="DbUpdateException">The update fails.</exception>
        public int Update(IEnumerable<TEntity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            return fDatabase.ExecuteOperation(context =>
            {
                List<TEntity> items = ValidateEntities(context, entities);
                Type concreteType = BlueprintDatabase.ResolveType<TEntity>(context);
                IReadOnlyList<IProperty> keys = context.Model.FindEntityType(concreteType)!.FindPrimaryKey()!.Properties;
                List<(TEntity Source, object Stored)> updated = new List<(TEntity Source, object Stored)>();
                try
                {
                    foreach (TEntity entity in items)
                    {
                        object?[] values = keys.Select(key => key.PropertyInfo!.GetValue(entity)).ToArray();
                        object? stored = context.Find(concreteType, values);
                        if (stored == null)
                        {
                            continue;
                        }
                        EntityEntry entry = context.Entry(stored);
                        entry.CurrentValues.SetValues(entity);
                        foreach (IProperty property in entry.Metadata.GetProperties().Where(property => property.IsConcurrencyToken))
                        {
                            entry.Property(property.Name).OriginalValue = property.PropertyInfo!.GetValue(entity);
                        }
                        entry.State = EntityState.Modified;
                        updated.Add((entity, stored));
                    }
                    int count = context.SaveChanges();
                    foreach ((TEntity source, object stored) in updated)
                    {
                        foreach (IProperty property in context.Entry(stored).Metadata.GetProperties().Where(property => property.ValueGenerated != ValueGenerated.Never))
                        {
                            property.PropertyInfo!.SetValue(source, property.PropertyInfo.GetValue(stored));
                        }
                    }
                    return count;
                }
                finally
                {
                    context.ChangeTracker.Clear();
                    fDatabase.InvalidateCache();
                }
            });
        }

        /// <summary>Deletes matching rows directly in SQL Server, translating multilingual reads and explicit text searches.</summary>
        /// <param name="predicate">The required delete filter.</param>
        /// <returns>The number of rows deleted.</returns>
        public int Delete(Expression<Func<TEntity, bool>> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return fDatabase.ExecuteOperation(context =>
            {
                _ = context.EntityTypes.getLanguage(fLanguage);
                Expression translated = new MultilingualQuery(fLanguage, context.EntityTypes.DefaultLanguageID).Visit(predicate)!;
                Expression<Func<TEntity, bool>> filter = (Expression<Func<TEntity, bool>>)new TextSearchQuery().Visit(translated)!;
                try
                {
                    return context.Query<TEntity>(false).Where(filter).ExecuteDelete();
                }
                finally
                {
                    fDatabase.InvalidateCache();
                }
            });
        }

        /// <summary>Validates types before ID generation or persistence begins.</summary>
        /// <param name="context">The active context.</param>
        /// <param name="entities">The entities that will be persisted.</param>
        /// <returns>The validated entities.</returns>
        /// <exception cref="ArgumentException">An entity is null or was not generated by BlueprintDatabase.</exception>
        private static List<TEntity> ValidateEntities(EntityContext context, IEnumerable<TEntity> entities)
        {
            Type type = BlueprintDatabase.ResolveType<TEntity>(context);
            List<TEntity> items = entities.ToList();
            if (items.Any(entity => entity == null || entity.GetType() != type))
            {
                throw new ArgumentException("Entities must be created through their Entity API Create() method.", nameof(entities));
            }
            return items;
        }

    }
}
