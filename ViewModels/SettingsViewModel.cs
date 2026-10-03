using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SocketMobileGroceryScanner.Data;
using SocketMobileGroceryScanner.Models;
using SocketMobileGroceryScanner.Services;
using Microsoft.EntityFrameworkCore;

namespace SocketMobileGroceryScanner.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly IScannerService _scanner;
    private readonly IMicrosoftToDoService _toDo;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IProductLookupService _productLookup;
    private bool _suppressListSelectionHandling;

    [ObservableProperty] private ScannerStatus _scannerStatus = ScannerStatus.Disconnected;
    [ObservableProperty] private string _scannerName = "Not connected";
    [ObservableProperty] private bool _isToDoSignedIn;
    [ObservableProperty] private string _toDoUserName = string.Empty;
    [ObservableProperty] private string _cacheInfo = string.Empty;
    [ObservableProperty] private ObservableCollection<ToDoListInfo> _availableLists = [];
    [ObservableProperty] private ToDoListInfo? _selectedList;
    [ObservableProperty] private bool _isTranslating;
    [ObservableProperty] private bool _isFetchingImages;

    partial void OnSelectedListChanged(ToDoListInfo? value)
    {
        if (_suppressListSelectionHandling || value == null) return;
        _ = _toDo.SelectListAsync(value.Id, value.Name);
    }

    public SettingsViewModel(
        IScannerService scanner,
        IMicrosoftToDoService toDo,
        IDbContextFactory<AppDbContext> dbFactory,
        IProductLookupService productLookup)
    {
        _scanner = scanner;
        _toDo = toDo;
        _dbFactory = dbFactory;
        _productLookup = productLookup;
        Title = "Settings";

        _scanner.StatusChanged += (_, s) =>
        {
            ScannerStatus = s;
            ScannerName = s == ScannerStatus.Connected ? _scanner.ScannerName : "Not connected";
        };

        ScannerStatus = _scanner.Status;
        ScannerName = _scanner.Status == ScannerStatus.Connected ? _scanner.ScannerName : "Not connected";
        IsToDoSignedIn = _toDo.IsSignedIn;
        ToDoUserName = _toDo.UserDisplayName ?? string.Empty;
    }

    [RelayCommand]
    public async Task LoadToDoListsAsync()
    {
        if (!IsToDoSignedIn) return;

        var lists = await _toDo.GetAvailableListsAsync();

        _suppressListSelectionHandling = true;
        AvailableLists = new ObservableCollection<ToDoListInfo>(lists);
        SelectedList = _toDo.SelectedListId != null
            ? AvailableLists.FirstOrDefault(l => l.Id == _toDo.SelectedListId)
            : null;
        _suppressListSelectionHandling = false;
    }

    [RelayCommand]
    private async Task CreateDefaultListAsync()
    {
        if (!IsToDoSignedIn) return;
        var created = await _toDo.CreateListAsync("Groceries");
        await LoadToDoListsAsync();

        _suppressListSelectionHandling = true;
        SelectedList = AvailableLists.FirstOrDefault(l => l.Id == created.Id);
        _suppressListSelectionHandling = false;
    }

    [RelayCommand]
    public async Task LoadCacheInfoAsync()
    {
        if (!IsToDoSignedIn)
        {
            await _toDo.TryRestoreSessionAsync();
            IsToDoSignedIn = _toDo.IsSignedIn;
            ToDoUserName = _toDo.UserDisplayName ?? string.Empty;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var count = await db.Products.CountAsync();
        CacheInfo = $"{count} products cached";

        if (IsToDoSignedIn)
            await LoadToDoListsAsync();
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Products.RemoveRange(db.Products);
        await db.SaveChangesAsync();
        CacheInfo = "Cache cleared";
    }

    [RelayCommand]
    private async Task RetranslateNamesAsync()
    {
        if (IsTranslating) return;
        IsTranslating = true;
        try
        {
            var count = await _productLookup.RetranslateCachedProductNamesAsync();
            CacheInfo = count > 0 ? $"Translated {count} product name(s)" : "No product names needed translation";
        }
        catch (Exception ex)
        {
            CacheInfo = "Translation failed — try again later";
            System.Diagnostics.Debug.WriteLine($"[Settings] Retranslate failed: {ex.Message}");
        }
        finally { IsTranslating = false; }
    }

    [RelayCommand]
    private async Task FetchMissingImagesAsync()
    {
        if (IsFetchingImages) return;
        IsFetchingImages = true;
        try
        {
            var count = await _productLookup.FetchMissingImagesAsync();
            CacheInfo = count > 0 ? $"Found images for {count} product(s)" : "No products were missing an image";
        }
        catch (Exception ex)
        {
            CacheInfo = "Image lookup failed — try again later";
            System.Diagnostics.Debug.WriteLine($"[Settings] FetchMissingImages failed: {ex.Message}");
        }
        finally { IsFetchingImages = false; }
    }

    [RelayCommand]
    private async Task SignInToDoAsync()
    {
        var ok = await _toDo.SignInAsync();
        if (ok)
        {
            IsToDoSignedIn = true;
            ToDoUserName = _toDo.UserDisplayName ?? string.Empty;
            await LoadToDoListsAsync();
        }
    }

    [RelayCommand]
    private async Task SignOutToDoAsync()
    {
        await _toDo.SignOutAsync();
        IsToDoSignedIn = false;
        AvailableLists = [];
        SelectedList = null;
        ToDoUserName = string.Empty;
    }

    [RelayCommand]
    private async Task OpenScannerConnectAsync()
    {
        await Shell.Current.GoToAsync("scannerconnect");
    }
}
