using SocketMobileGroceryScanner.Models;

namespace SocketMobileGroceryScanner.Services;

public interface IProductLookupService
{
    Task<Product?> LookupAsync(string barcode, CancellationToken ct = default);

    /// <summary>
    /// Retranslates cached product names that are still in a non-Dutch language
    /// (including ones that predate translation support). Returns the number updated.
    /// </summary>
    Task<int> RetranslateCachedProductNamesAsync(CancellationToken ct = default);

    /// <summary>
    /// Attempts to find a fallback image for cached products that have none
    /// (e.g. manually-added items, or barcodes with no photo). Returns the number updated.
    /// </summary>
    Task<int> FetchMissingImagesAsync(CancellationToken ct = default);

    /// <summary>
    /// Fixes any already-cached image URLs that were mangled by a network proxy
    /// rewriting Wikipedia API responses in transit (no network calls; pure data
    /// repair). Safe and cheap to run on every startup. Returns the number fixed.
    /// </summary>
    Task<int> RepairBrokenImageUrlsAsync(CancellationToken ct = default);
}
