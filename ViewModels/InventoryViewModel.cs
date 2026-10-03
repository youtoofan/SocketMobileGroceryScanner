using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SocketMobileGroceryScanner.Models;
using SocketMobileGroceryScanner.Services;

namespace SocketMobileGroceryScanner.ViewModels;

public partial class InventoryViewModel : BaseViewModel
{
    private readonly IInventoryService _inventory;
    private readonly IScannerService _scanner;

    [ObservableProperty] private ObservableCollection<InventoryItem> _items = [];
    [ObservableProperty] private bool _scanModeActive;
    [ObservableProperty] private string _lastScanMessage = string.Empty;
    [ObservableProperty] private ScannerStatus _scannerStatus = ScannerStatus.Disconnected;
    [ObservableProperty] private string _scannerName = string.Empty;
    [ObservableProperty] private string _testBarcodeInput = string.Empty;

    public InventoryViewModel(IInventoryService inventory, IScannerService scanner)
    {
        _inventory = inventory;
        _scanner = scanner;
        Title = "Inventory";

        _scanner.StatusChanged += OnScannerStatusChanged;
        ScannerStatus = _scanner.Status;
        ScannerName = _scanner.ScannerName;
    }

    public void SubscribeToScanner() => _scanner.BarcodeScanned += OnBarcodeScanned;
    public void UnsubscribeFromScanner() => _scanner.BarcodeScanned -= OnBarcodeScanned;

    private void OnScannerStatusChanged(object? sender, ScannerStatus s)
    {
        ScannerStatus = s;
        ScannerName = _scanner.ScannerName;
    }

    private async void OnBarcodeScanned(object? sender, string barcode)
    {
        if (!ScanModeActive) return;
        LastScanMessage = $"Scanning {barcode}…";
        var item = await _inventory.AddOrIncrementAsync(barcode);
        if (item == null) { LastScanMessage = $"Product not found: {barcode}"; return; }
        var existing = Items.FirstOrDefault(i => i.Id == item.Id);
        if (existing != null) { var idx = Items.IndexOf(existing); Items[idx] = item; }
        else Items.Insert(0, item);
        LastScanMessage = $"✓ {item.Product.DisplayName} — qty {item.Quantity}";
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var all = await _inventory.GetAllAsync();
            Items = new ObservableCollection<InventoryItem>(all);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ToggleScanMode() => ScanModeActive = !ScanModeActive;

    [RelayCommand]
    private async Task IncrementAsync(InventoryItem item)
    {
        var updated = await _inventory.AddOrIncrementAsync(item.Product.Barcode);
        if (updated == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0) Items[idx] = updated;
    }

    [RelayCommand]
    private async Task DecrementAsync(InventoryItem item)
    {
        var updated = await _inventory.DecrementAsync(item.Id);
        if (updated == null) { Items.Remove(item); return; }
        var idx = Items.IndexOf(item);
        if (idx >= 0) Items[idx] = updated;
    }

    [RelayCommand]
    private async Task TestScanAsync()
    {
        var barcode = TestBarcodeInput.Trim();
        if (string.IsNullOrEmpty(barcode)) return;
        TestBarcodeInput = string.Empty;
        _scanner.SimulateScan(barcode);
    }
}
