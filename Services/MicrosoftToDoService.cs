using SocketMobileGroceryScanner.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Authentication;

namespace SocketMobileGroceryScanner.Services;

public class MicrosoftToDoService : IMicrosoftToDoService
{
    private const string DefaultListName = "Groceries";
    private const string SelectedListIdKey = "MsToDo.SelectedListId";
    private const string SelectedListNameKey = "MsToDo.SelectedListName";
    private const string ManualBarcodePrefix = "manual:";

    private readonly MsalAuthService _auth;
    private readonly IShoppingListService _shoppingList;
    private readonly IInventoryService _inventory;
    private GraphServiceClient? _graphClient;

    public bool IsSignedIn { get; private set; }
    public string? UserDisplayName { get; private set; }
    public string? SelectedListId { get; private set; }
    public string? SelectedListName { get; private set; }

    public MicrosoftToDoService(MsalAuthService auth, IShoppingListService shoppingList, IInventoryService inventory)
    {
        _auth = auth;
        _shoppingList = shoppingList;
        _inventory = inventory;

        SelectedListId = Preferences.Default.Get(SelectedListIdKey, (string?)null);
        SelectedListName = Preferences.Default.Get(SelectedListNameKey, (string?)null);
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        if (IsSignedIn) return true;

        try
        {
            var result = await _auth.AcquireTokenSilentAsync();
            if (result == null) return false;

            _graphClient = new GraphServiceClient(
                new BaseBearerTokenAuthenticationProvider(
                    new TokenProvider(_auth)));

            UserDisplayName = result.Account.Username;
            IsSignedIn = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SignInAsync()
    {
        var result = await _auth.AcquireTokenAsync();
        if (result == null) return false;

        _graphClient = new GraphServiceClient(
            new BaseBearerTokenAuthenticationProvider(
                new TokenProvider(_auth)));

        UserDisplayName = result.Account.Username;
        IsSignedIn = true;
        return true;
    }

    public async Task SignOutAsync()
    {
        await _auth.SignOutAsync();
        _graphClient = null;
        IsSignedIn = false;
        UserDisplayName = null;
    }

    public async Task<List<ToDoListInfo>> GetAvailableListsAsync(CancellationToken ct = default)
    {
        if (_graphClient == null) return [];

        var lists = await _graphClient.Me.Todo.Lists.GetAsync(cancellationToken: ct);
        return (lists?.Value ?? [])
            .Where(l => l.Id != null)
            .Select(l => new ToDoListInfo(l.Id!, l.DisplayName ?? "(untitled)"))
            .ToList();
    }

    public Task SelectListAsync(string listId, string listName, CancellationToken ct = default)
    {
        SelectedListId = listId;
        SelectedListName = listName;
        Preferences.Default.Set(SelectedListIdKey, listId);
        Preferences.Default.Set(SelectedListNameKey, listName);
        return Task.CompletedTask;
    }

    public async Task<ToDoListInfo> CreateListAsync(string name, CancellationToken ct = default)
    {
        if (_graphClient == null) throw new InvalidOperationException("Not signed in.");

        var created = await _graphClient.Me.Todo.Lists.PostAsync(
            new TodoTaskList { DisplayName = name },
            cancellationToken: ct);

        var info = new ToDoListInfo(created!.Id!, created.DisplayName ?? name);
        await SelectListAsync(info.Id, info.Name, ct);
        return info;
    }

    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken ct = default)
    {
        if (_graphClient == null || string.IsNullOrEmpty(taskId)) return false;

        try
        {
            var listId = await ResolveListIdAsync(ct);
            await _graphClient.Me.Todo.Lists[listId].Tasks[taskId].DeleteAsync(cancellationToken: ct);
            return true;
        }
        catch (Exception ex)
        {
            // Task may already be gone (e.g. deleted remotely too) — not fatal.
            System.Diagnostics.Debug.WriteLine($"[ToDoSync] DeleteTask failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SyncShoppingListAsync(CancellationToken ct = default)
    {
        if (_graphClient == null) return false;

        try
        {
            var listId = await ResolveListIdAsync(ct);

            var localItems = await _shoppingList.GetAllAsync();
            var remoteTasks = await GetTasksAsync(listId, ct);

            // --- 1. Inbound: tasks completed in Microsoft To Do ---
            await ProcessCompletedTasksAsync(listId, localItems, remoteTasks, ct);

            // --- 2. Pull: tasks removed directly in Microsoft To Do -> remove the linked local item ---
            localItems = await _shoppingList.GetAllAsync();
            remoteTasks = await GetTasksAsync(listId, ct);
            var activeRemoteById = ActiveById(remoteTasks);

            foreach (var item in localItems.Where(i => !i.IsFound && !string.IsNullOrEmpty(i.ToDoTaskId)))
            {
                if (!activeRemoteById.ContainsKey(item.ToDoTaskId!))
                    await _shoppingList.RemoveAsync(item.Id);
            }

            // --- 3. Pull: new tasks added directly in Microsoft To Do -> add as local items ---
            localItems = await _shoppingList.GetAllAsync();
            var linkedTaskIds = localItems
                .Where(i => !string.IsNullOrEmpty(i.ToDoTaskId))
                .Select(i => i.ToDoTaskId!)
                .ToHashSet();

            foreach (var task in activeRemoteById.Values)
            {
                if (linkedTaskIds.Contains(task.Id!)) continue;

                var (name, qty) = ParseTitle(task.Title ?? "Unnamed item");
                var created = await _shoppingList.AddManualItemAsync(name, qty);
                await _shoppingList.SetToDoTaskIdAsync(created.Id, task.Id);
            }

            // --- 4. Push: local items without a remote task -> create one ---
            localItems = await _shoppingList.GetAllAsync();
            var needed = localItems.Where(i => !i.IsFound).ToList();

            foreach (var item in needed.Where(i => string.IsNullOrEmpty(i.ToDoTaskId)))
            {
                var title = BuildTitle(item);
                var createdTask = await _graphClient.Me.Todo.Lists[listId].Tasks
                    .PostAsync(new TodoTask { Title = title }, cancellationToken: ct);
                await _shoppingList.SetToDoTaskIdAsync(item.Id, createdTask!.Id);
            }

            // --- 5. Push: keep titles/quantities in sync for already-linked items ---
            foreach (var item in needed.Where(i => !string.IsNullOrEmpty(i.ToDoTaskId)))
            {
                if (activeRemoteById.TryGetValue(item.ToDoTaskId!, out var remoteTask))
                {
                    var title = BuildTitle(item);
                    if (remoteTask.Title != title)
                        await _graphClient.Me.Todo.Lists[listId].Tasks[item.ToDoTaskId!]
                            .PatchAsync(new TodoTask { Title = title }, cancellationToken: ct);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToDoSync] {ex.Message}");
            return false;
        }
    }

    private async Task<List<TodoTask>> GetTasksAsync(string listId, CancellationToken ct)
    {
        var response = await _graphClient!.Me.Todo.Lists[listId].Tasks.GetAsync(cancellationToken: ct);
        return response?.Value ?? [];
    }

    private static Dictionary<string, TodoTask> ActiveById(List<TodoTask> tasks) =>
        tasks
            .Where(t => t.Id != null && t.Status != Microsoft.Graph.Models.TaskStatus.Completed)
            .ToDictionary(t => t.Id!, t => t);

    /// <summary>
    /// Finds tasks completed in Microsoft To Do, marks the matching shopping list
    /// items as done, adds them to inventory, and deletes the completed tasks.
    /// </summary>
    private async Task ProcessCompletedTasksAsync(
        string listId,
        List<ShoppingListItem> localItems,
        List<TodoTask> remoteTasks,
        CancellationToken ct)
    {
        var completedTasks = remoteTasks
            .Where(t => t.Status == Microsoft.Graph.Models.TaskStatus.Completed && t.Id != null)
            .ToList();

        if (completedTasks.Count == 0) return;

        // Prefer matching by the linked task id; fall back to name matching for
        // items that predate task-id tracking.
        var itemByTaskId = localItems
            .Where(i => !string.IsNullOrEmpty(i.ToDoTaskId))
            .ToDictionary(i => i.ToDoTaskId!, i => i);

        var itemByName = localItems
            .Where(i => !i.IsFound && string.IsNullOrEmpty(i.ToDoTaskId) && !string.IsNullOrEmpty(i.Product?.Name))
            .GroupBy(i => i.Product.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var task in completedTasks)
        {
            ShoppingListItem? match = null;
            if (!itemByTaskId.TryGetValue(task.Id!, out match) && !string.IsNullOrWhiteSpace(task.Title))
            {
                var (name, _) = ParseTitle(task.Title);
                itemByName.TryGetValue(name, out match);
            }

            if (match != null && !match.IsFound)
            {
                await _shoppingList.MarkFoundAsync(match.Id, true);

                // Skip synthetic (Microsoft To Do only) items when updating inventory.
                if (match.Product?.Barcode != null && !match.Product.Barcode.StartsWith(ManualBarcodePrefix))
                    await _inventory.AddOrIncrementAsync(match.Product.Barcode);
            }

            // Always clean up the completed task from To Do.
            await _graphClient!.Me.Todo.Lists[listId].Tasks[task.Id!]
                .DeleteAsync(cancellationToken: ct);
        }
    }

    // "Milk x2" → ("Milk", 2), "Milk" → ("Milk", 1)
    private static (string Name, int Qty) ParseTitle(string title)
    {
        var match = System.Text.RegularExpressions.Regex.Match(title, @"^(.*?)\s+x(\d+)$");
        if (match.Success && int.TryParse(match.Groups[2].Value, out var qty) && qty > 0)
            return (match.Groups[1].Value.Trim(), qty);
        return (title.Trim(), 1);
    }

    private static string BuildTitle(ShoppingListItem item)
    {
        var productName = !string.IsNullOrWhiteSpace(item.Product?.Name)
            ? item.Product.Name
            : $"Product ({item.Product?.Barcode})";

        return item.NeededQty > 1 ? $"{productName} x{item.NeededQty}" : productName;
    }

    private async Task<string> ResolveListIdAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(SelectedListId))
            return SelectedListId!;

        var lists = await _graphClient!.Me.Todo.Lists.GetAsync(cancellationToken: ct);
        var existing = lists?.Value?.FirstOrDefault(l =>
            string.Equals(l.DisplayName, DefaultListName, StringComparison.OrdinalIgnoreCase));

        string listId;
        string listName;

        if (existing?.Id != null)
        {
            listId = existing.Id;
            listName = existing.DisplayName ?? DefaultListName;
        }
        else
        {
            var created = await _graphClient.Me.Todo.Lists.PostAsync(
                new TodoTaskList { DisplayName = DefaultListName },
                cancellationToken: ct);
            listId = created!.Id!;
            listName = DefaultListName;
        }

        await SelectListAsync(listId, listName, ct);
        return listId;
    }

    private sealed class TokenProvider : IAccessTokenProvider
    {
        private readonly MsalAuthService _auth;
        public TokenProvider(MsalAuthService auth) => _auth = auth;
        public AllowedHostsValidator AllowedHostsValidator { get; } = new AllowedHostsValidator();
        public async Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken ct = default)
        {
            var result = await _auth.AcquireTokenAsync();
            return result?.AccessToken ?? string.Empty;
        }
    }
}
