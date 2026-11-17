using Blueprint.Database.Entities;
using Blueprint.Database.Entities.Annotations;
using Microsoft.EntityFrameworkCore;

namespace Blueprint.Tests.RAMCache
{
    /// <summary>Ορίζει έναν ανεξάρτητο cached πίνακα για ελέγχους συγχρονισμού με εξωτερικές SQL αλλαγές.</summary>
    [Entity("RAMCachedRecords"), RAMCached]
    public interface CachedRecord : BlueprintEntity
    {
        [Precision(18, 2)]
        decimal Price { get; set; }

        [Multilingual]
        string? Title { get; set; }
    }
}
