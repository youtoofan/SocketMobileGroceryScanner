using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SocketMobileGroceryScanner.Resources.Strings;
using SocketMobileGroceryScanner.Services;

namespace SocketMobileGroceryScanner.ViewModels;

public partial class ScannerConnectViewModel : BaseViewModel
{
    private readonly IScannerService _scanner;

    [ObservableProperty] private ObservableCollection<BluetoothDeviceInfo> _devices = [];
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _connectionStatusText = AppResources.Scanner_Initializing;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _testBarcode = string.Empty;
    [ObservableProperty] private bool _hasTestBarcode;

    // No longer used but kept for interface compat
    [ObservableProperty] private BluetoothDeviceInfo? _selectedDevice;
    [ObservableProperty] private bool _canConnect;

    public bool IsDisconnected => !IsConnected && !IsConnecting;

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(IsDisconnected));
    partial void OnIsConnectingChanged(bool value) => OnPropertyChanged(nameof(IsDisconnected));
    partial void OnErrorMessageChanged(string value) => HasError = !string.IsNullOrEmpty(value);

    public ScannerConnectViewModel(IScannerService scanner)
    {
        _scanner = scanner;
        Title = AppResources.Scanner_Title;

        UpdateConnectionState(_scanner.Status);

        _scanner.StatusChanged += (_, status) =>
            MainThread.BeginInvokeOnMainThread(() => UpdateConnectionState(status));

        _scanner.BarcodeScanned += (_, barcode) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TestBarcode = barcode;
                HasTestBarcode = true;
            });
    }

    private void UpdateConnectionState(ScannerStatus status)
    {
        IsConnected = status == ScannerStatus.Connected;
        IsConnecting = status == ScannerStatus.Connecting;
        ConnectionStatusText = status switch
        {
            ScannerStatus.Connected => string.Format(AppResources.Scanner_ConnectedTo, _scanner.ScannerName),
            ScannerStatus.Connecting => AppResources.Scanner_Connecting,
            _ => AppResources.Scanner_NoScanner
        };
        ErrorMessage = _scanner.LastError;
        RefreshDeviceList();
    }

    [RelayCommand]
    public async Task LoadDevicesAsync()
    {
        ErrorMessage = string.Empty;
        HasTestBarcode = false;
        TestBarcode = string.Empty;

        try
        {
            // Always (re-)initialize — the service handles close+reopen internally
            await _scanner.InitializeAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(AppResources.Scanner_InitFailed, ex.Message);
        }

        RefreshDeviceList();
    }

    private void RefreshDeviceList()
    {
        _ = Task.Run(async () =>
        {
            var devices = await _scanner.GetPairedDevicesAsync();
            MainThread.BeginInvokeOnMainThread(() =>
                Devices = new ObservableCollection<BluetoothDeviceInfo>(devices));
        });
    }

    [RelayCommand]
    private Task ConnectAsync() => Task.CompletedTask; // Companion handles connections

    [RelayCommand]
    private Task DisconnectAsync() => Task.CompletedTask; // Companion handles connections

    [RelayCommand]
    private async Task OpenCompanionAppAsync()
    {
        try
        {
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                // Launch Socket Mobile Companion on Android
                await Launcher.Default.OpenAsync("https://play.google.com/store/apps/details?id=com.socketmobile.companion");
            }
            else
            {
                await Launcher.Default.OpenAsync("https://www.socketmobile.com/support/downloads?categories=companion");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(AppResources.Scanner_CompanionOpenFailed, ex.Message);
        }
    }
}
