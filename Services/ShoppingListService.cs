using SocketMobileGroceryScanner.Data;
using SocketMobileGroceryScanner.Models;
using Microsoft.EntityFrameworkCore;

namespace SocketMobileGroceryScanner.Services;

public class ShoppingListService : IShoppingListService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IProductLookupService _lookup;
    private readonly IImageFallbackService _imageFallback;

    public ShoppingListService(IDbContextFactory<AppDbContext> dbFactory, IProductLookupService lookup, IImageFallbackService imageFallback)
    {
        _dbFactory = dbFactory;
        _lookup = lookup;
        _imageFallback = imageFallback;
    }

    public async Task<List<ShoppingListItem>> GetAllAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ShoppingListItems
            .AsNoTracking()
            .Include(s => s.Product)
            .OrderBy(s => s.IsFound)
            .ThenBy(s => s.Product.Name)
            .ToListAsync();
    }

    public async Task<ShoppingListItem?> AddOrIncrementNeededAsync(string barcode)
    {
        var product = await _lookup.LookupAsync(barcode);
        if (product == null) return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.ShoppingListItems.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProductId == product.Id);

        if (existing != null)
        {
            // If item was previously found (checked off), reset qty to 1 instead of incrementing
            var newQty = existing.IsFound ? 1 : existing.NeededQty + 1;
            await db.ShoppingListItems
                .Where(s => s.Id == existing.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.NeededQty, newQty)
                    .SetProperty(i => i.IsFound, false));
            existing.NeededQty = newQty;
            existing.IsFound = false;
            existing.Product = product;
            return existing;
        }

        var item = new ShoppingListItem { ProductId = product.Id, NeededQty = 1, IsFound = false, DateAdded = DateTime.UtcNow };
        db.ShoppingListItems.Add(item);
        await db.SaveChangesAsync();
        item.Product = product;
        return item;
    }

    public async Task MarkFoundAsync(int id, bool isFound)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.ShoppingListItems
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsFound, isFound));
    }

    public async Task RemoveAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.ShoppingListItems.Where(s => s.Id == id).ExecuteDeleteAsync();
    }

    public async Task ClearFoundAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.ShoppingListItems.Where(s => s.IsFound).ExecuteDeleteAsync();
    }

    public async Task ResetAllToUnfoundAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.ShoppingListItems.ExecuteUpdateAsync(s => s.SetProperty(i => i.IsFound, false));
    }

    public async Task<ShoppingListItem> AddManualItemAsync(string name, int neededQty = 1)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Try a free fallback image (e.g. Wikipedia) for items added without a
        // barcode (manually, or imported from Microsoft To Do).
        // NOTE: per MAUI docs, SVG assets (Resources\Images\placeholder_product.svg)
        // are converted to PNG at build time and MUST be referenced with a ".png"
        // extension from C#/XAML — using ".svg" or no extension fails to resolve on
        // every platform and renders as blank space.
        var imageUrl = await _imageFallback.TryFindImageAsync(name) ?? "placeholder_product.png";

        // Synthetic barcode so manually-added (Microsoft To Do only) items fit the
        // existing Product/ShoppingListItem schema without a real barcode.
        var product = new Product
        {
            Barcode = $"manual:{Guid.NewGuid():N}",
            Name = name,
            Brand = string.Empty,
            ImageUrl = imageUrl,
            LastFetched = DateTime.UtcNow
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var item = new ShoppingListItem
        {
            ProductId = product.Id,
            NeededQty = Math.Max(1, neededQty),
            IsFound = false,
            DateAdded = DateTime.UtcNow
        };
        db.ShoppingListItems.Add(item);
        await db.SaveChangesAsync();
        item.Product = product;
        return item;
    }

    public async Task SetToDoTaskIdAsync(int id, string? toDoTaskId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.ShoppingListItems
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ToDoTaskId, toDoTaskId));
    }
}
