namespace SocketMobileGroceryScanner.Services;

public interface IImageFallbackService
{
    /// <summary>
    /// Attempts to find a free, representative image for a product that has no
    /// photo of its own (e.g. not found in Open Food Facts, or added manually).
    /// Returns null if nothing suitable was found.
    /// </summary>
    Task<string?> TryFindImageAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Downloads a previously-found remote Wikimedia image URL and caches it
    /// locally, returning the local file path. Used to repair products whose
    /// ImageUrl still points at a remote Wikimedia URL (saved before local
    /// caching was added) so they render without hitting the 403 that
    /// Wikimedia's CDN returns to clients without a proper User-Agent.
    /// Returns null if the download fails.
    /// </summary>
    Task<string?> CacheRemoteImageAsync(string remoteUrl, CancellationToken ct = default);
}
