// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Microsoft.EntityFrameworkCore;

namespace Blueprint.Tests.RAMCache
{
    /// <summary>Ορίζει έναν ανεξάρτητο cached πίνακα για ελέγχους συγχρονισμού με εξωτερικές SQL αλλαγές.</summary>
    [Entity("RAMCachedRecords")]
    public abstract class CachedRecord : BlueprintEntityRAM
    {
        [Precision(18, 2)]
        public abstract decimal Price { get; set; }

        [Multilingual]
        public abstract string? Title { get; set; }
    }
}
