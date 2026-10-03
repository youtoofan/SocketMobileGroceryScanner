using SocketMobileGroceryScanner.Data;
using SocketMobileGroceryScanner.Models;
using Microsoft.EntityFrameworkCore;

namespace SocketMobileGroceryScanner.Services;

public class InventoryService : IInventoryService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IProductLookupService _lookup;

    public InventoryService(IDbContextFactory<AppDbContext> dbFactory, IProductLookupService lookup)
    {
        _dbFactory = dbFactory;
        _lookup = lookup;
    }

    public async Task<List<InventoryItem>> GetAllAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.InventoryItems
            .AsNoTracking()
            .Include(i => i.Product)
            .OrderBy(i => i.Product.Name)
            .ToListAsync();
    }

    public async Task<InventoryItem?> AddOrIncrementAsync(string barcode)
    {
        var product = await _lookup.LookupAsync(barcode);
        if (product == null) return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.InventoryItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductId == product.Id);

        if (existing != null)
        {
            await db.InventoryItems
                .Where(i => i.Id == existing.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Quantity, existing.Quantity + 1)
                    .SetProperty(i => i.LastUpdated, DateTime.UtcNow));
            existing.Quantity++;
            existing.Product = product;
            return existing;
        }

        var item = new InventoryItem { ProductId = product.Id, Quantity = 1, LastUpdated = DateTime.UtcNow };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        item.Product = product;
        return item;
    }

    public async Task<InventoryItem?> DecrementAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var item = await db.InventoryItems.AsNoTracking()
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (item == null) return null;

        if (item.Quantity <= 1)
        {
            await db.InventoryItems.Where(i => i.Id == id).ExecuteDeleteAsync();
            return null;
        }

        await db.InventoryItems
            .Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Quantity, item.Quantity - 1)
                .SetProperty(i => i.LastUpdated, DateTime.UtcNow));
        item.Quantity--;
        return item;
    }

    public async Task<InventoryItem?> DecrementByBarcodeAsync(string barcode)
    {
        // Look up product id from barcode, then decrement the inventory item
        await using var db = await _dbFactory.CreateDbContextAsync();
        var item = await db.InventoryItems.AsNoTracking()
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.Product.Barcode == barcode);

        if (item == null) return null;
        return await DecrementAsync(item.Id);
    }

    public async Task RemoveAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.InventoryItems.Where(i => i.Id == id).ExecuteDeleteAsync();
    }
}
