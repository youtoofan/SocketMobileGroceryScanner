using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SocketMobileGroceryScanner.Models;
using SocketMobileGroceryScanner.Services;

namespace SocketMobileGroceryScanner.ViewModels;

public partial class ShoppingListViewModel : BaseViewModel
{
    private readonly IShoppingListService _shoppingList;
    private readonly IInventoryService _inventory;
    private readonly IMicrosoftToDoService _toDo;
    private readonly IScannerService _scanner;
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    [ObservableProperty] private ObservableCollection<ShoppingListItem> _needed = [];
    [ObservableProperty] private ObservableCollection<ShoppingListItem> _found = [];
    [ObservableProperty] private ScannerStatus _scannerStatus = ScannerStatus.Disconnected;
    [ObservableProperty] private string _scannerName = string.Empty;
    [ObservableProperty] private string _lastScanMessage = string.Empty;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private bool _isToDoSignedIn;
    [ObservableProperty] private string _toDoUserName = string.Empty;
    [ObservableProperty] private string _testBarcodeInput = string.Empty;

    public bool CanSync => IsToDoSignedIn && !IsSyncing;

    partial void OnIsSyncingChanged(bool value) => OnPropertyChanged(nameof(CanSync));
    partial void OnIsToDoSignedInChanged(bool value) => OnPropertyChanged(nameof(CanSync));

    public ShoppingListViewModel(
        IShoppingListService shoppingList,
        IInventoryService inventory,
        IMicrosoftToDoService toDo,
        IScannerService scanner)
    {
        _shoppingList = shoppingList;
        _inventory    = inventory;
        _toDo         = toDo;
        _scanner      = scanner;
        Title = "Shopping List";

        _scanner.StatusChanged += OnScannerStatusChanged;

        IsToDoSignedIn = _toDo.IsSignedIn;
        ToDoUserName   = _toDo.UserDisplayName ?? string.Empty;
    }

    public void SubscribeToScanner()
    {
        _scanner.BarcodeScanned += OnBarcodeScanned;
        StartPolling();
    }

    public void UnsubscribeFromScanner()
    {
        _scanner.BarcodeScanned -= OnBarcodeScanned;
        StopPolling();
    }

    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        _ = PollToDoAsync(_pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollToDoAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                if (_toDo.IsSignedIn && !IsSyncing)
                {
                    await AutoSyncAsync();
                    await LoadAsync();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Poll] {ex.Message}");
            }
        }
    }

    private void OnScannerStatusChanged(object? sender, ScannerStatus s)
    {
        ScannerStatus = s;
        ScannerName   = _scanner.ScannerName;
    }

    private async void OnBarcodeScanned(object? sender, string barcode) =>
        await HandleBarcodeAsync(barcode);

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!IsToDoSignedIn)
            {
                await _toDo.TryRestoreSessionAsync();
                IsToDoSignedIn = _toDo.IsSignedIn;
                ToDoUserName = _toDo.UserDisplayName ?? string.Empty;
            }

            var all = await _shoppingList.GetAllAsync();
            Needed = new ObservableCollection<ShoppingListItem>(all.Where(i => !i.IsFound));
            Found  = new ObservableCollection<ShoppingListItem>(all.Where(i => i.IsFound));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task MarkFoundAsync(ShoppingListItem item)
    {
        await _shoppingList.MarkFoundAsync(item.Id, !item.IsFound);
        item.IsFound = !item.IsFound;
        if (item.IsFound) { Needed.Remove(item); Found.Insert(0, item); }
        else { Found.Remove(item); Needed.Insert(0, item); }
    }

    [RelayCommand]
    private async Task ClearFoundAsync()
    {
        await _shoppingList.ClearFoundAsync();
        Found.Clear();
    }

    [RelayCommand]
    private async Task ResetListAsync()
    {
        await _shoppingList.ResetAllToUnfoundAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveItemAsync(ShoppingListItem item)
    {
        var linkedTaskId = item.ToDoTaskId;
        await _shoppingList.RemoveAsync(item.Id);
        Needed.Remove(item);
        Found.Remove(item);

        // Delete the linked Microsoft To Do task right away rather than relying on
        // the next full sync to infer the removal.
        if (!string.IsNullOrEmpty(linkedTaskId) && _toDo.IsSignedIn)
            _ = _toDo.DeleteTaskAsync(linkedTaskId);

        _ = AutoSyncAsync();
    }

    [RelayCommand]
    private async Task TestScanAsync()
    {
        var barcode = TestBarcodeInput.Trim();
        if (string.IsNullOrEmpty(barcode)) return;
        TestBarcodeInput = string.Empty;
        _scanner.SimulateScan(barcode);
    }

    [RelayCommand]
    private async Task SignInToDoAsync()
    {
        var ok = await _toDo.SignInAsync();
        if (!ok) return;
        IsToDoSignedIn = true;
        ToDoUserName   = _toDo.UserDisplayName ?? string.Empty;
        _ = AutoSyncAsync();
    }

    private async Task HandleBarcodeAsync(string barcode)
    {
        LastScanMessage = $"Scanning {barcode}…";
        var result = await _shoppingList.AddOrIncrementNeededAsync(barcode);
        if (result == null) { LastScanMessage = $"Product not found: {barcode}"; return; }

        var existingNeeded = Needed.FirstOrDefault(i => i.Id == result.Id);
        if (existingNeeded != null) { var idx = Needed.IndexOf(existingNeeded); Needed[idx] = result; }
        else
        {
            var existingFound = Found.FirstOrDefault(i => i.Id == result.Id);
            if (existingFound != null) Found.Remove(existingFound);
            Needed.Insert(0, result);
        }
        LastScanMessage = $"📋 {result.Product.DisplayName} — need {result.NeededQty}";
        _ = _inventory.DecrementByBarcodeAsync(barcode);
        _ = AutoSyncAsync();
    }

    [RelayCommand]
    private async Task AutoSyncAsync()
    {
        if (!_toDo.IsSignedIn) return;
        if (!_syncLock.Wait(0)) return; // skip if sync already in progress
        ToDoUserName = _toDo.UserDisplayName;
        IsSyncing = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _toDo.SyncShoppingListAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[Sync] Timed out after 30s");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
            _syncLock.Release();
        }
    }
}
