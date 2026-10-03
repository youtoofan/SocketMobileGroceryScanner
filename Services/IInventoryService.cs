using SocketMobileGroceryScanner.Models;

namespace SocketMobileGroceryScanner.Services;

public interface IInventoryService
{
    Task<List<InventoryItem>> GetAllAsync();
    Task<InventoryItem?> AddOrIncrementAsync(string barcode);
    Task<InventoryItem?> DecrementAsync(int inventoryItemId);
    /// <summary>Finds the inventory item by barcode and decrements it. Returns null if not in inventory.</summary>
    Task<InventoryItem?> DecrementByBarcodeAsync(string barcode);
    Task RemoveAsync(int inventoryItemId);
}
