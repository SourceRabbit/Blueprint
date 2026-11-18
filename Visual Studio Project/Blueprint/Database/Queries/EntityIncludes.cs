using Blueprint.Database.Entities.Annotations;
using Blueprint.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Blueprint.Database.Queries
{
    /// <summary>Builds Include paths for declared relationships while breaking cycles.</summary>
    internal static class EntityIncludes
    {
        /// <summary>Enables loading of annotated relationships for complete entities.</summary>
        /// <typeparam name="TEntity">The generated entity class.</typeparam>
        /// <param name="context">The query context.</param>
        /// <param name="query">The original query.</param>
        /// <returns>The query with its Includes.</returns>
        internal static IQueryable<TEntity> Apply<TEntity>(BlueprintDatabaseContext context, IQueryable<TEntity> query) where TEntity : class
        {
            foreach (var path in GetPaths(context, context.Model.FindEntityType(typeof(TEntity))!, string.Empty, new HashSet<Type>()))
            {
                query = query.Include(path);
            }
            return query;
        }

        /// <summary>Follows AutoInclude relationships without repeating a type within the same path.</summary>
        /// <param name="context">The model context.</param>
        /// <param name="entityType">The current EF type.</param>
        /// <param name="prefix">The path from the root.</param>
        /// <param name="ancestors">The types already visited.</param>
        /// <returns>The loading paths.</returns>
        /// <exception cref="InvalidOperationException">AutoInclude refers to an unmapped relationship.</exception>
        private static IEnumerable<string> GetPaths(BlueprintDatabaseContext context, IEntityType entityType, string prefix, HashSet<Type> ancestors)
        {
            if (!ancestors.Add(entityType.ClrType))
            {
                yield break;
            }
            var contract = context.EntityTypes.Types.Single(pair => pair.Value == entityType.ClrType).Key;
            foreach (var property in BlueprintEntityTypes.GetProperties(contract).Where(property => property.IsDefined(typeof(AutoInclude), true)))
            {
                var navigation = entityType.FindNavigation(property.Name) ?? throw new InvalidOperationException($"AutoInclude property '{contract}.{property.Name}' must be a mapped navigation.");
                var path = prefix + property.Name;
                yield return path;
                foreach (var child in GetPaths(context, navigation.TargetEntityType, path + ".", new HashSet<Type>(ancestors)))
                {
                    yield return child;
                }
            }
        }
    }
}
