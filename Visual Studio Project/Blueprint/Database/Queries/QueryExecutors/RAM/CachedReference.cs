// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database.Sql.RAMCache;
using Blueprint.Entities;

namespace Blueprint.Database.Queries.QueryExecutors.RAM
{
    /// <summary>Shares one target snapshot between sorting and loading the references on the selected entities.</summary>
    internal sealed class CachedReference
    {
        private readonly RAMCachedTable fTable;
        private readonly IReadOnlyDictionary<long, BlueprintEntity> fRows;

        /// <summary>Captures the target's current immutable rows for this execution.</summary>
        /// <param name="table">The synchronized target table.</param>
        internal CachedReference(RAMCachedTable table)
        {
            fTable = table;
            fRows = table.Rows;
        }

        /// <summary>Finds the shared row used when reading a scalar ordering key.</summary>
        /// <param name="id">The root row's foreign key.</param>
        /// <returns>The cached row or null when the reference is absent.</returns>
        internal BlueprintEntity? FindRow(long? id)
        {
            return id.HasValue && fRows.TryGetValue(id.Value, out BlueprintEntity? row) ? row : null;
        }

        /// <summary>Copies the requested references from the same snapshot that determined the page order.</summary>
        /// <param name="ids">The distinct target IDs needed by the selected rows.</param>
        /// <param name="language">The language of the returned entities.</param>
        /// <returns>Independent copies for the result graph.</returns>
        internal IEnumerable<BlueprintEntity> CopyRows(long[] ids, long language)
        {
            foreach (long id in ids)
            {
                if (fRows.TryGetValue(id, out BlueprintEntity? row))
                {
                    yield return fTable.Copy(row, language);
                }
            }
        }
    }
}
