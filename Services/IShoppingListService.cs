using SocketMobileGroceryScanner.Models;

namespace SocketMobileGroceryScanner.Services;

public interface IShoppingListService
{
    Task<List<ShoppingListItem>> GetAllAsync();

    /// <summary>Adds the product to the list, or increments NeededQty if already there.</summary>
    Task<ShoppingListItem?> AddOrIncrementNeededAsync(string barcode);

    /// <summary>Toggle the IsFound flag on a shopping list item (manual check-off).</summary>
    Task MarkFoundAsync(int id, bool isFound);

    Task RemoveAsync(int id);

    /// <summary>Removes only the done (IsFound) items from the shopping list.</summary>
    Task ClearFoundAsync();

    Task ResetAllToUnfoundAsync();

    /// <summary>
    /// Adds an item that originated outside the app (e.g. added directly in Microsoft To Do),
    /// creating a synthetic product so it fits the existing Product/ShoppingListItem model.
    /// </summary>
    Task<ShoppingListItem> AddManualItemAsync(string name, int neededQty = 1);

    /// <summary>Links (or unlinks, when null) a shopping list item to its Microsoft To Do task.</summary>
    Task SetToDoTaskIdAsync(int id, string? toDoTaskId);
}
