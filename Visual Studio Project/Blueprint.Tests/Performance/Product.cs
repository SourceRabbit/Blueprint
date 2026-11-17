using Blueprint.Database.Entities;
using Blueprint.Database.Entities.Annotations;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Blueprint.Tests.Performance
{
    /// <summary>Χαρτογραφεί όλα τα πεδία και τις μεταφράσεις του υπάρχοντος πίνακα Products της DemoEShop.</summary>
    [Entity("website_ecommerce_products"), RAMCached]
    public interface Product : BlueprintEntity
    {
        [DefaultValue(0)]
        int Type { get; set; }

        long? ParentProductID { get; set; }

        bool IsActive { get; set; }

        [MaxLength(64)]
        string SKU { get; set; }

        [MaxLength(64)]
        string? EAN { get; set; }

        [Precision(18, 2)]
        decimal? NetPrice { get; set; }

        long VatCategoryID { get; set; }

        [Multilingual, MaxLength(256)]
        string? Title { get; set; }

        [Multilingual, MaxLength(512)]
        string? ShortDescription { get; set; }

        [Multilingual]
        string? Description { get; set; }

        [Multilingual, MaxLength(256)]
        string? Slug { get; set; }

        long? BrandID { get; set; }

        int? Stock { get; set; }

        long? SizeID { get; set; }

        long? ColorID { get; set; }

        int SortOrder { get; set; }

        bool IsDefaultVariant { get; set; }

        [Precision(18, 3)]
        decimal? Weight { get; set; }

        [Column(TypeName = "datetime2(0)")]
        DateTime CreatedDate { get; set; }

        [Column(TypeName = "datetime2(0)")]
        DateTime ModifiedDate { get; set; }
    }
}
