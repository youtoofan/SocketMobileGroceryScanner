using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocketMobileGroceryScanner.Models;

public class Product
{
    [Key]
    public int Id { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Category { get; set; }
    public string? Quantity { get; set; } // e.g. "500g", "1L"
    public DateTime LastFetched { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Language of Name as last set (2-letter code, e.g. "fr", "de"). Null/"nl" means
    /// no (further) translation is needed. Used to retry translation for cached
    /// products that were stored before translation support was added, or whose
    /// translation attempt failed.
    /// </summary>
    public string? Lang { get; set; }

    /// <summary>
    /// User-friendly name that never returns blank — falls back to barcode.
    /// </summary>
    [NotMapped]
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name
        : !string.IsNullOrWhiteSpace(Barcode) ? $"Product ({Barcode})"
        : "Unknown product";
}
