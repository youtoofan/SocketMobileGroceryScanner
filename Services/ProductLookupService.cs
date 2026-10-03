using System.Text.Json;
using SocketMobileGroceryScanner.Data;
using SocketMobileGroceryScanner.Models;
using Microsoft.EntityFrameworkCore;

namespace SocketMobileGroceryScanner.Services;

public class ProductLookupService : IProductLookupService
{
    private const string ManualBarcodePrefix = "manual:";

    // MAUI's build pipeline (Resizetizer) converts Resources\Images\*.svg files to
    // PNG at build time. Per MAUI docs, such images must be referenced from C#/XAML
    // using a ".png" extension (NOT ".svg", and NOT extension-less) — either of the
    // wrong forms fails to resolve on every platform and renders as blank space.
    private const string PlaceholderImage = "placeholder_product.png";

    // Old (broken) values some already-saved products still have, from before this
    // was understood: the original literal ".svg" reference, and a later attempt
    // that incorrectly stripped the extension entirely.
    private static readonly string[] LegacyBrokenPlaceholders = { "placeholder_product.svg", "placeholder_product" };
    private static readonly TimeSpan CacheAge = TimeSpan.FromDays(30);
    private static readonly SemaphoreSlim _saveLock = new(1, 1);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly HttpClient _http;
    private readonly ITranslationService _translation;
    private readonly IImageFallbackService _imageFallback;

    public ProductLookupService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHttpClientFactory httpFactory,
        ITranslationService translation,
        IImageFallbackService imageFallback)
    {
        _dbFactory = dbFactory;
        _http = httpFactory.CreateClient("ProductApi");
        _translation = translation;
        _imageFallback = imageFallback;
    }

    public async Task<Product?> LookupAsync(string barcode, CancellationToken ct = default)
    {
        // 1. Check local cache
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cached = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Barcode == barcode, ct);

        if (cached != null && DateTime.UtcNow - cached.LastFetched < CacheAge)
            return cached;

        // 2. Fetch from external APIs
        var product = await FetchFromOpenFoodFactsAsync(barcode, ct)
                   ?? await FetchFromUpcItemDbAsync(barcode, ct);

        if (product == null) return cached;

        // 3. Ensure Name is never empty (some barcodes return only an image)
        NormalizeProduct(product, barcode);

        // 4. Translate a non-Dutch name, and fill in a fallback image if none was found
        await TranslateNameAsync(product, ct);
        await FillFallbackImageAsync(product, ct);

        // 5. Upsert — serialised to avoid concurrent INSERTs for the same barcode
        await _saveLock.WaitAsync(ct);
        try
        {
            await using var upsertDb = await _dbFactory.CreateDbContextAsync(ct);
            var existing = await upsertDb.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Barcode == barcode, ct);

            if (existing != null)
            {
                product.Id          = existing.Id;
                product.Barcode     = barcode;
                product.LastFetched = DateTime.UtcNow;
                upsertDb.Products.Update(product);
            }
            else
            {
                product.Barcode     = barcode;
                product.LastFetched = DateTime.UtcNow;
                upsertDb.Products.Add(product);
            }

            await upsertDb.SaveChangesAsync(ct);
            return product;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task<int> RetranslateCachedProductNamesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // Lang is null for manually-typed/Microsoft To Do items (source language was
        // never known) — still attempt translation for those via auto-detection.
        var candidates = await db.Products
            .Where(p => p.Lang == null || p.Lang != "nl")
            .ToListAsync(ct);

        var updated = 0;
        foreach (var product in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (await TranslateNameAsync(product, ct))
            {
                db.Products.Update(product);
                updated++;
            }
        }

        if (updated > 0) await db.SaveChangesAsync(ct);
        return updated;
    }

    public async Task<int> FetchMissingImagesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var candidates = await db.Products
            .Where(p => p.ImageUrl == null || p.ImageUrl == "" || LegacyBrokenPlaceholders.Contains(p.ImageUrl))
            .ToListAsync(ct);

        var updated = 0;
        foreach (var product in candidates)
        {
            ct.ThrowIfCancellationRequested();
            // Force a re-attempt for items stuck on an old broken placeholder value.
            if (LegacyBrokenPlaceholders.Contains(product.ImageUrl)) product.ImageUrl = null;
            if (await FillFallbackImageAsync(product, ct))
            {
                db.Products.Update(product);
                updated++;
            }
        }

        updated += await RepairBrokenWikimediaUrlsInternalAsync(db, ct);

        if (updated > 0) await db.SaveChangesAsync(ct);
        return updated;
    }

    public async Task<int> RepairBrokenImageUrlsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var fixedCount = await RepairBrokenWikimediaUrlsInternalAsync(db, ct);

        // Cheap, no-network fix for the old invalid placeholder values (wrong/missing
        // extension means the MAUI-compiled resource never resolved and rendered blank).
        var legacyPlaceholders = await db.Products
            .Where(p => p.ImageUrl != null && LegacyBrokenPlaceholders.Contains(p.ImageUrl))
            .ToListAsync(ct);
        foreach (var product in legacyPlaceholders)
        {
            product.ImageUrl = PlaceholderImage;
            db.Products.Update(product);
            fixedCount++;
        }

        if (fixedCount > 0) await db.SaveChangesAsync(ct);
        return fixedCount;
    }

    /// <summary>
    /// Some network proxies rewrite Wikipedia API responses in transit, replacing the
    /// real "upload.wikimedia.org" thumbnail host with an unreachable one and adding
    /// tracking query parameters. Also, Wikimedia's CDN rejects image requests with no
    /// User-Agent (which is what MAUI's native Image control sends), so any ImageUrl
    /// still pointing at a remote wikimedia.org URL — however well-formed — needs to be
    /// downloaded and cached locally. This repairs both problems for already-saved data.
    /// </summary>
    private async Task<int> RepairBrokenWikimediaUrlsInternalAsync(Data.AppDbContext db, CancellationToken ct)
    {
        var broken = await db.Products
            .Where(p => p.ImageUrl != null && p.ImageUrl.Contains("wikimedia.org"))
            .ToListAsync(ct);

        var fixedCount = 0;
        foreach (var product in broken)
        {
            if (!Uri.TryCreate(product.ImageUrl, UriKind.Absolute, out var uri)) continue;

            var builder = new UriBuilder(uri) { Host = "upload.wikimedia.org", Query = string.Empty };
            var normalizedUrl = builder.Uri.ToString();

            var localPath = await _imageFallback.CacheRemoteImageAsync(normalizedUrl, ct);
            if (localPath != null)
            {
                product.ImageUrl = localPath;
                db.Products.Update(product);
                fixedCount++;
            }
        }

        return fixedCount;
    }

    /// <summary>
    /// Translates Product.Name to Dutch if Lang indicates it isn't already.
    /// Returns true if the name changed.
    /// </summary>
    private async Task<bool> TranslateNameAsync(Product product, CancellationToken ct)
    {
        if (product.Lang == "nl") return false;
        if (string.IsNullOrWhiteSpace(product.Name)) return false;

        // product.Lang may be null (e.g. manually-typed/Microsoft To Do items) — pass
        // it through as-is; TranslationService falls back to auto-detection.
        var translated = await _translation.TranslateToDutchAsync(product.Name, product.Lang, ct);
        if (translated == null || translated == product.Name) return false;

        product.Name = translated;
        product.Lang = "nl"; // mark as done so it isn't retranslated again
        return true;
    }

    /// <summary>
    /// Finds a free fallback image when the product has none. Returns true if one was set.
    /// </summary>
    private async Task<bool> FillFallbackImageAsync(Product product, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(product.ImageUrl)) return false;

        // Nothing meaningful to search for synthetic (Microsoft To Do only) products
        // beyond their user-typed name — still worth trying, it's free.
        var query = !string.IsNullOrWhiteSpace(product.Name) ? product.Name : product.Brand;
        if (string.IsNullOrWhiteSpace(query) || query.StartsWith(ManualBarcodePrefix)) return false;

        var found = await _imageFallback.TryFindImageAsync(query, ct);
        product.ImageUrl = found ?? PlaceholderImage;
        return true;
    }

    private async Task<Product?> FetchFromOpenFoodFactsAsync(string barcode, CancellationToken ct)
    {
        try
        {
            var url = $"https://be-nl.openfoodfacts.org/api/v2/product/{barcode}.json?fields=product_name,brands,image_front_small_url,categories_tags,quantity,lang&lc=nl";
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("status").GetInt32() != 1) return null;

            var p = root.GetProperty("product");
            return new Product
            {
                Name = GetString(p, "product_name") ?? string.Empty,
                Brand = GetString(p, "brands") ?? string.Empty,
                ImageUrl = GetString(p, "image_front_small_url"),
                Category = p.TryGetProperty("categories_tags", out var tags) && tags.GetArrayLength() > 0
                    ? tags[0].GetString()?.Replace("en:", "") : null,
                Quantity = GetString(p, "quantity"),
                // "lang" is the product's original language on Open Food Facts; when it
                // isn't Dutch, product_name often falls back to that language too.
                Lang = GetString(p, "lang"),
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenFoodFacts] {ex.Message}");
            return null;
        }
    }

    private async Task<Product?> FetchFromUpcItemDbAsync(string barcode, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.upcitemdb.com/prod/trial/lookup?upc={barcode}";
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("items", out var items) || items.GetArrayLength() == 0) return null;

            var item = items[0];
            return new Product
            {
                Name = GetString(item, "title") ?? string.Empty,
                Brand = GetString(item, "brand") ?? string.Empty,
                ImageUrl = item.TryGetProperty("images", out var imgs) && imgs.GetArrayLength() > 0
                    ? imgs[0].GetString() : null,
                Category = GetString(item, "category"),
                // UpcItemDb doesn't report a language; its trial/US dataset is English.
                Lang = "en",
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpcItemDb] {ex.Message}");
            return null;
        }
    }

    private static string? GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() : null;

    /// <summary>
    /// Fills in Product.Name when the API returned only partial data (e.g. image but no title).
    /// Fallback order: Brand → Category → "Product (barcode)".
    /// </summary>
    private static void NormalizeProduct(Product product, string barcode)
    {
        if (!string.IsNullOrWhiteSpace(product.Name))
            return;

        if (!string.IsNullOrWhiteSpace(product.Brand))
        {
            product.Name = product.Brand;
            return;
        }

        if (!string.IsNullOrWhiteSpace(product.Category))
        {
            product.Name = product.Category
                .Replace("-", " ")
                .Replace("_", " ")
                .Trim();
            return;
        }

        product.Name = $"Product ({barcode})";
    }
}
