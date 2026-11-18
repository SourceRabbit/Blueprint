using Blueprint.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Blueprint.Entities
{
    /// <summary>Provides shared query, insert, update, and delete syntax for types deriving from BlueprintEntity.</summary>
    public static class BlueprintEntityQueries
    {
        extension<TEntity>(TEntity) where TEntity : BlueprintEntity
        {
            /// <summary>Creates an entity instance through the active database without persisting it.</summary>
            /// <returns>The new instance of the generated class.</returns>
            public static TEntity Create()
            {
                return BlueprintDatabase.ACTIVE_INSTANCE.Create<TEntity>();
            }

            /// <summary>Creates a filtered query for the entity type in the active Blueprint database without reading rows.</summary>
            /// <param name="predicate">The filter to translate to SQL through EF Core.</param>
            /// <returns>An entity query that supports further LINQ composition and executes when results are read.</returns>
            /// <exception cref="ArgumentNullException">The filter is null.</exception>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            public static IQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
            {
                ArgumentNullException.ThrowIfNull(predicate);
                return BlueprintDatabase.ACTIVE_INSTANCE.Query<TEntity>().Where(predicate);
            }

            /// <summary>Creates a query in the active Blueprint database with a specific language for filters and results.</summary>
            /// <param name="predicate">The filter to translate to SQL through EF Core.</param>
            /// <param name="language">The language of Multilingual properties.</param>
            /// <returns>A query that supports further LINQ composition before execution.</returns>
            /// <exception cref="ArgumentNullException">The filter is null.</exception>
            /// <exception cref="ArgumentOutOfRangeException">The language ID was not configured during initialization.</exception>
            /// <exception cref="InvalidOperationException">No Blueprint is active, or the type has not been initialized.</exception>
            /// <exception cref="ObjectDisposedException">The active database has been disposed.</exception>
            public static IQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate, long language)
            {
                ArgumentNullException.ThrowIfNull(predicate);
                return BlueprintDatabase.ACTIVE_INSTANCE.Query<TEntity>(language).Where(predicate);
            }

            /// <summary>Returns one entity row from the active Blueprint database without loading the entire table.</summary>
            /// <returns>One row, or null if the table is empty.</returns>
            /// <remarks>Without ordering, the returned row is not guaranteed.</remarks>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Reading from the database fails.</exception>
            public static TEntity? FirstOrDefault()
            {
                return BlueprintDatabase.ACTIVE_INSTANCE.Query<TEntity>().FirstOrDefault();
            }

            /// <summary>Returns the first row matching the filter by executing the query through EF Core.</summary>
            /// <param name="predicate">The filter to translate to SQL.</param>
            /// <returns>The first matching row, or null if no result exists.</returns>
            /// <remarks>If multiple rows match, the returned row is not guaranteed without ordering.</remarks>
            /// <exception cref="ArgumentNullException">The filter is null.</exception>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Reading from the database fails.</exception>
            public static TEntity? FirstOrDefault(Expression<Func<TEntity, bool>> predicate)
            {
                ArgumentNullException.ThrowIfNull(predicate);
                return BlueprintDatabase.ACTIVE_INSTANCE.Query<TEntity>().FirstOrDefault(predicate);
            }

            /// <summary>Returns the single entity row or null, rejecting multiple rows.</summary>
            /// <param name="language">The BlueprintLanguage.ID, or null for the language with an empty Prefix.</param>
            /// <returns>The single entity, or null if the table is empty.</returns>
            /// <exception cref="ArgumentOutOfRangeException">The language ID was not configured during initialization.</exception>
            /// <exception cref="InvalidOperationException">Multiple rows exist, or the active database or type has not been initialized.</exception>
            /// <exception cref="ObjectDisposedException">The active database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Reading from the database fails.</exception>
            public static TEntity? SingleOrDefault(long? language = null)
            {
                return BlueprintDatabase.ACTIVE_INSTANCE.Query<TEntity>(language).SingleOrDefault();
            }

            /// <summary>Returns the single matching row or null without selecting an arbitrary result.</summary>
            /// <param name="predicate">The filter to translate to SQL through EF Core.</param>
            /// <param name="language">The BlueprintLanguage.ID, or null for the language with an empty Prefix.</param>
            /// <returns>The single matching entity, or null if none exists.</returns>
            /// <exception cref="ArgumentNullException">The filter is null.</exception>
            /// <exception cref="ArgumentOutOfRangeException">The language ID was not configured during initialization.</exception>
            /// <exception cref="InvalidOperationException">Multiple rows match, or the active database or type has not been initialized.</exception>
            /// <exception cref="ObjectDisposedException">The active database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Reading from the database fails.</exception>
            public static TEntity? SingleOrDefault(Expression<Func<TEntity, bool>> predicate, long? language = null)
            {
                ArgumentNullException.ThrowIfNull(predicate);
                return BlueprintDatabase.ACTIVE_INSTANCE.Query<TEntity>(language).SingleOrDefault(predicate);
            }

            /// <summary>Checks whether the active Blueprint database contains any rows without loading entities.</summary>
            /// <returns>True if at least one row exists; otherwise false.</returns>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Checking for rows in the database fails.</exception>
            public static bool Any()
            {
                return BlueprintDatabase.ACTIVE_INSTANCE.Query<TEntity>().Any();
            }

            /// <summary>Checks whether a row matches the filter in the active Blueprint database without loading entities.</summary>
            /// <param name="predicate">The filter to translate to SQL through EF Core.</param>
            /// <returns>True if at least one matching row exists; otherwise false.</returns>
            /// <exception cref="ArgumentNullException">The filter is null.</exception>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Checking for rows in the database fails.</exception>
            public static bool Any(Expression<Func<TEntity, bool>> predicate)
            {
                ArgumentNullException.ThrowIfNull(predicate);
                return BlueprintDatabase.ACTIVE_INSTANCE.Query<TEntity>().Any(predicate);
            }

            /// <summary>Inserts an entity into the active Blueprint database through its SQL commands.</summary>
            /// <param name="entity">The entity to insert.</param>
            /// <returns>The number of inserted rows.</returns>
            /// <exception cref="ArgumentNullException">The entity is null.</exception>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Inserting into the database fails.</exception>
            public static int Insert(TEntity entity)
            {
                ArgumentNullException.ThrowIfNull(entity);
                return BlueprintDatabase.ACTIVE_INSTANCE.Commands<TEntity>().Insert(entity);
            }

            /// <summary>Inserts a collection of entities into the active Blueprint database through their SQL commands.</summary>
            /// <param name="entities">The entities to insert.</param>
            /// <returns>The number of inserted rows, or zero for an empty collection.</returns>
            /// <exception cref="ArgumentNullException">The collection is null.</exception>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Inserting into the database fails.</exception>
            public static int Insert(IEnumerable<TEntity> entities)
            {
                ArgumentNullException.ThrowIfNull(entities);
                return BlueprintDatabase.ACTIVE_INSTANCE.Commands<TEntity>().Insert(entities);
            }

            /// <summary>Updates a row's stored fields in the active Blueprint database using the entity's primary key.</summary>
            /// <param name="entity">The entity containing the row's primary key and new field values.</param>
            /// <returns>The number of updated rows, or zero if the row was not found.</returns>
            /// <exception cref="ArgumentNullException">The entity is null.</exception>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active, or the entity has no primary key defined.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Updating the database fails.</exception>
            public static int Update(TEntity entity)
            {
                ArgumentNullException.ThrowIfNull(entity);
                return BlueprintDatabase.ACTIVE_INSTANCE.Commands<TEntity>().Update(entity);
            }

            /// <summary>Updates multiple rows in the active Blueprint database using each entity's primary key and values.</summary>
            /// <param name="entities">The entities containing the rows' primary keys and new field values.</param>
            /// <returns>The total number of updated rows, or zero for an empty collection or when no rows were found.</returns>
            /// <exception cref="ArgumentNullException">The collection is null.</exception>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active, or the type has no primary key defined.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Updating the database fails.</exception>
            public static int Update(IEnumerable<TEntity> entities)
            {
                ArgumentNullException.ThrowIfNull(entities);
                return BlueprintDatabase.ACTIVE_INSTANCE.Commands<TEntity>().Update(entities);
            }

            /// <summary>Permanently deletes matching rows from the active Blueprint database without loading entities.</summary>
            /// <param name="predicate">The required filter that determines which rows to delete.</param>
            /// <returns>The number of deleted rows, or zero if no rows matched.</returns>
            /// <exception cref="ArgumentNullException">The filter is null.</exception>
            /// <exception cref="ArgumentException">The type is not a registered BlueprintEntity class.</exception>
            /// <exception cref="InvalidOperationException">No initialized Blueprint is active.</exception>
            /// <exception cref="ObjectDisposedException">The active Blueprint's database has been disposed.</exception>
            /// <exception cref="System.Data.Common.DbException">Deleting from the database fails.</exception>
            public static int Delete(Expression<Func<TEntity, bool>> predicate)
            {
                ArgumentNullException.ThrowIfNull(predicate);
                return BlueprintDatabase.ACTIVE_INSTANCE.Commands<TEntity>().Delete(predicate);
            }
        }
    }
}

