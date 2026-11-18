// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using System.ComponentModel.DataAnnotations.Schema;

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
    }
}
