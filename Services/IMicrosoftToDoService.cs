using SocketMobileGroceryScanner.Models;

namespace SocketMobileGroceryScanner.Services;

public interface IMicrosoftToDoService
{
    bool IsSignedIn { get; }
    string? UserDisplayName { get; }

    /// <summary>Id of the Microsoft To Do list currently used for syncing, if one has been selected.</summary>
    string? SelectedListId { get; }

    /// <summary>Display name of the currently selected list.</summary>
    string? SelectedListName { get; }

    Task<bool> TryRestoreSessionAsync();
    Task<bool> SignInAsync();
    Task SignOutAsync();
    Task<bool> SyncShoppingListAsync(CancellationToken ct = default);

    /// <summary>Returns all Microsoft To Do lists available in the signed-in account.</summary>
    Task<List<ToDoListInfo>> GetAvailableListsAsync(CancellationToken ct = default);

    /// <summary>Selects (and persists) the list to sync with.</summary>
    Task SelectListAsync(string listId, string listName, CancellationToken ct = default);

    /// <summary>Creates a new Microsoft To Do list and selects it.</summary>
    Task<ToDoListInfo> CreateListAsync(string name, CancellationToken ct = default);

    /// <summary>Deletes a single remote task immediately, e.g. when a local item is removed.</summary>
    Task<bool> DeleteTaskAsync(string taskId, CancellationToken ct = default);
}
