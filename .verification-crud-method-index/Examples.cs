using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.ComponentModel.DataAnnotations;
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Database.Queries;
using Microsoft.EntityFrameworkCore;

namespace CrudExamples;
/// <summary>Describes a product with multilingual content, a price, and stock quantity.</summary>
[Entity("Products")]
public abstract class Product : BlueprintEntityRAM
{
    public abstract string SKU { get; set; }

    [Multilingual]
    public abstract string Title { get; set; }

    [Multilingual]
    public abstract string ShortDescription { get; set; }

    [Multilingual]
    public abstract string Description { get; set; }

    [Precision(18, 2)]
    public abstract decimal Price { get; set; }

    public abstract int Stock { get; set; }

    [Timestamp]
    public abstract byte[] Version { get; set; }
}
internal static class Examples
{
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example1(long productID)
    {
        IQueryable<Product> query = Product.Where(item => item.Price >= 50m);
        List<Product> products = query.Where(item => item.Stock > 0).OrderBy(item => item.ID).ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example2(long productID)
    {
        IQueryable<string> query = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID).Select(item => item.SKU);
        List<string> skus = query.ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example3(long productID)
    {
        IQueryable<Product> query = Product.Where(item => item.Price > 50m);
        IOrderedQueryable<Product> cheapestFirst = query.OrderBy(item => item.Price);
        IOrderedQueryable<Product> mostExpensiveFirst = query.OrderByDescending(item => item.Price);
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example4(long productID)
    {
        List<Product> products = Product.Where(item => item.Price > 50m)
            .OrderBy(item => item.Price)
            .ThenByDescending(item => item.Stock)
            .ThenBy(item => item.ID)
            .ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example5(long productID)
    {
        List<Product> secondPage = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID).Skip(20).Take(20).ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example6(long productID)
    {
        IQueryable<Product> query = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID);
        List<Product> products = query.ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example7(long productID)
    {
        
        decimal minimumPrice = 50m;
        int pageIndex = 0;
        int pageSize = 20;
        List<Product> products = Product.Where(item => item.Price >= minimumPrice)
            .OrderBy(item => item.Price)
            .ThenBy(item => item.ID)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example8(long productID)
    {
        
        string search = "φορεμα";
        List<Product> products = Product.Where(item => item.Title.Contains(search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace))
            .OrderBy(item => item.ID)
            .Skip(0)
            .Take(25)
            .ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example9(long productID)
    {
        bool matchesCase = "Bodystocking".Contains("body", CompareOptions.IgnoreCase); // true
        bool matchesGreekCase = "Φόρεμα".Contains("φόρεμα", CompareOptions.IgnoreCase); // true
        bool matchesMissingAccent = "Φόρεμα".Contains("φορεμα", CompareOptions.IgnoreCase); // false
        
        List<Product> products = Product.Where(item => item.Title.Contains("body", CompareOptions.IgnoreCase)).OrderBy(item => item.ID).Take(25).ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example10(long productID)
    {
        bool matchesAccent = "Φόρεμα".Contains("Φορεμα", CompareOptions.IgnoreNonSpace); // true
        bool matchesDifferentCase = "Φόρεμα".Contains("φορεμα", CompareOptions.IgnoreNonSpace); // Runtime-dependent; see below.
        
        List<Product> products = Product.Where(item => item.Title.Contains("Φορεμα", CompareOptions.IgnoreNonSpace)).OrderBy(item => item.ID).Take(25).ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example11(long productID)
    {
        CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        bool mixedCase = "Φόρεμα".Contains("φορεμα", options); // true
        bool uppercase = "ΦΟΡΕΜΑ".Contains("φόρεμα", options); // true
        bool insideTitle = "Καλοκαιρινό Φόρεμα".Contains("φορεμα", options); // true
        
        string search = "φορεμα";
        IQueryable<Product> query = Product.Where(item => item.Title.Contains(search, options), 1);
        int totalCount = query.Count();
        List<Product> page = query.OrderBy(item => item.ID).Skip(0).Take(25).ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example12(long productID)
    {
        bool ignoresCase = "Bodystocking".StartsWith("body", CompareOptions.IgnoreCase); // true
        bool keepsAccent = "Φόρεμα".StartsWith("φορεμα", CompareOptions.IgnoreCase); // false
        bool ignoresAccent = "Φόρεμα".StartsWith("Φορεμα", CompareOptions.IgnoreNonSpace); // true
        bool matchesDifferentCase = "Φόρεμα".StartsWith("φορεμα", CompareOptions.IgnoreNonSpace); // Runtime-dependent, as with Contains.
        
        CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        bool prefix = "Φόρεμα καλοκαιρινό".StartsWith("φορεμα", options); // true
        bool insideTitle = "Καλοκαιρινό φόρεμα".StartsWith("φορεμα", options); // false
        bool contains = "Καλοκαιρινό φόρεμα".Contains("φορεμα", options); // true
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example13(long productID)
    {
        string search = "φορεμα";
        CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        List<Product> products = Product.Where(item => item.Title.Contains(search, options))
            .OrderByDescending(item => item.Title.StartsWith(search, options))
            .ThenBy(item => item.Title)
            .ThenBy(item => item.ID)
            .Take(6)
            .ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example14(long productID)
    {
        Product? first = Product.FirstOrDefault(item => item.Price > 50m);
        Product? cheapest = Product.Where(item => item.Price > 50m).OrderBy(item => item.Price).ThenBy(item => item.ID).FirstOrDefault();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example15(long productID)
    {
        Product? product = Product.SingleOrDefault(item => item.ID == productID);
        Product? translatedProduct = Product.SingleOrDefault(item => item.ID == productID, 2);
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example16(long productID)
    {
        bool hasProducts = Product.Any();
        bool exists = Product.Any(item => item.SKU == "CAMERA-001");
        bool hasAffordableStock = Product.Where(item => item.Price < 200m).Any(item => item.Stock > 0);
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example17(long productID)
    {
        IQueryable<Product> query = Product.Where(item => item.Price > 50m);
        int count = query.Count();
        long longCount = query.LongCount();
        int inStockCount = query.Count(item => item.Stock > 0);
        long inStockLongCount = query.LongCount(item => item.Stock > 0);
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example18(long productID)
    {
        Product? product = Product.SingleOrDefault(item => item.ID == productID);
        Product? first = Product.FirstOrDefault(item => item.Price > 50m);
        bool exists = Product.Any(item => item.SKU == "CAMERA-001");
        int count = Product.Where(item => item.Price > 50m).Count();
        List<string> skus = Product.Where(item => item.Price > 50m).OrderBy(item => item.ID).Select(item => item.SKU).ToList();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example19(long productID)
    {
        Product? frenchProduct = Product.SingleOrDefault(item => item.ID == productID, 2);
        List<Product> frenchProducts = Product.Where(item => item.Title == "Appareil photo", 2).ToList();
        Product? firstFrenchProduct = Product.Where(item => item.Price > 50m, 2).OrderBy(item => item.ID).FirstOrDefault();
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example20(long productID)
    {
        Product product = Product.Create();
        product.SKU = "CAMERA-DRAFT";
        product.Title = "Camera";
        product.Price = 120m;
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example21(long productID)
    {
        Product product = Product.Create();
        product.SKU = "CAMERA-001";
        product.Title = "Camera";
        product.Price = 120m;
        int insertedRows = Product.Insert(product);
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example22(long productID)
    {
        Product product = Product.SingleOrDefault(item => item.ID == productID) ?? throw new InvalidOperationException("Product was not found.");
        product.Price = 135m;
        int updatedRows = Product.Update(product);
    }
    /// <summary>Verifies that this manual example compiles against the current public API.</summary>
    /// <param name="productID">An existing product ID supplied by the application.</param>
    internal static void Example23(long productID)
    {
        int deletedRows = Product.Delete(item => item.ID == productID);
    }
}
