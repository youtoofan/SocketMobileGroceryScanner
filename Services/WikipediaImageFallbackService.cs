using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace SocketMobileGroceryScanner.Services;

/// <summary>
/// Free, keyless fallback image lookup using the public Wikipedia API
/// (no API key, no cost). Searches Dutch Wikipedia first, then English,
/// and returns the first matching article's thumbnail image.
/// </summary>
public class WikipediaImageFallbackService : IImageFallbackService
{
    private readonly HttpClient _http;
    private static readonly string CacheDir = Path.Combine(FileSystem.AppDataDirectory, "images");

    public WikipediaImageFallbackService(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("WikiApi");
    }

    public async Task<string?> TryFindImageAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var remoteUrl = await SearchWikipediaAsync("nl", query, ct)
            ?? await SearchWikipediaAsync("en", query, ct);
        if (remoteUrl == null) return null;

        // Wikimedia's image CDN rejects requests with no/blank User-Agent (403),
        // which is exactly what MAUI's native Image control sends on most platforms.
        // Download the bytes ourselves (our HttpClient sends a proper User-Agent)
        // and cache them locally so the Image control just loads a local file.
        return await DownloadAndCacheAsync(remoteUrl, ct) ?? remoteUrl;
    }

    public Task<string?> CacheRemoteImageAsync(string remoteUrl, CancellationToken ct = default) =>
        DownloadAndCacheAsync(remoteUrl, ct);

    private async Task<string?> SearchWikipediaAsync(string wiki, string query, CancellationToken ct)
    {
        try
        {
            var q = HttpUtility.UrlEncode(query);
            var url = $"https://{wiki}.wikipedia.org/w/api.php" +
                      $"?action=query&generator=search&gsrsearch={q}&gsrlimit=1" +
                      "&prop=pageimages&piprop=thumbnail&pithumbsize=400&format=json&origin=*";

            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("query", out var queryEl) ||
                !queryEl.TryGetProperty("pages", out var pages))
                return null;

            foreach (var page in pages.EnumerateObject())
            {
                if (page.Value.TryGetProperty("thumbnail", out var thumb) &&
                    thumb.TryGetProperty("source", out var source))
                {
                    return NormalizeWikimediaUrl(source.GetString());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFallback:{wiki}] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Some network proxies rewrite the Wikipedia API's JSON response in transit,
    /// swapping the real "upload.wikimedia.org" thumbnail host for a different
    /// (often unreachable) one and tacking on tracking query parameters. Force the
    /// well-known real host back and strip any injected query string so the image
    /// actually loads regardless of what the proxy returned.
    /// </summary>
    private static string? NormalizeWikimediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;

        if (!uri.Host.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Host = "upload.wikimedia.org",
                Query = string.Empty,
            };
            return builder.Uri.ToString();
        }

        return url;
    }

    /// <summary>
    /// Downloads the given image URL (using our own HttpClient, which sends a proper
    /// User-Agent) and saves it to local app storage. Returns the local file path, or
    /// null if the download failed (caller should fall back to a placeholder).
    /// </summary>
    private async Task<string?> DownloadAndCacheAsync(string remoteUrl, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);

            var ext = Path.GetExtension(new Uri(remoteUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5) ext = ".jpg";
            var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(remoteUrl))) + ext;
            var localPath = Path.Combine(CacheDir, fileName);

            if (File.Exists(localPath)) return localPath;

            var bytes = await _http.GetByteArrayAsync(remoteUrl, ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);
            return localPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFallback] Download failed for {remoteUrl}: {ex.Message}");
            return null;
        }
    }
}
