using Blueprint.Database.Entities;
using Blueprint.Database.Entities.Annotations;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Blueprint.Tests.Performance
{
    /// <summary>Χαρτογραφεί όλα τα πεδία και τις μεταφράσεις του υπάρχοντος πίνακα Products της DemoEShop.</summary>
    [Entity("website_ecommerce_products")]
    public abstract class Product : BlueprintEntityRAM
    {
        [DefaultValue(0)]
        public abstract int Type { get; set; }

        public abstract long? ParentProductID { get; set; }

        public abstract bool IsActive { get; set; }

        [MaxLength(64)]
        public abstract string SKU { get; set; }

        [MaxLength(64)]
        public abstract string? EAN { get; set; }

        [Precision(18, 2)]
        public abstract decimal? NetPrice { get; set; }

        public abstract long VatCategoryID { get; set; }

        [Multilingual, MaxLength(256)]
        public abstract string? Title { get; set; }

        [Multilingual, MaxLength(512)]
        public abstract string? ShortDescription { get; set; }

        [Multilingual]
        public abstract string? Description { get; set; }

        [Multilingual, MaxLength(256)]
        public abstract string? Slug { get; set; }

        public abstract long? BrandID { get; set; }

        public abstract int? Stock { get; set; }

        public abstract long? SizeID { get; set; }

        public abstract long? ColorID { get; set; }

        public abstract int SortOrder { get; set; }

        public abstract bool IsDefaultVariant { get; set; }

        [Precision(18, 3)]
        public abstract decimal? Weight { get; set; }

        [Column(TypeName = "datetime2(0)")]
        public abstract DateTime CreatedDate { get; set; }

        [Column(TypeName = "datetime2(0)")]
        public abstract DateTime ModifiedDate { get; set; }
    }
}
