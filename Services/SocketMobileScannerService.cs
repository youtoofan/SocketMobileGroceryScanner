using SocketMobile.Capture;

namespace SocketMobileGroceryScanner.Services;

public class SocketMobileScannerService : IScannerService
{
#if ANDROID
    private const string AppId = "android:com.socketmobile.simplecapturedemo";
#elif WINDOWS
    private const string AppId = "windows:com.socketmobile.singleentry";
#else
    private const string AppId = "ios:com.socketmobile.singleentry";
#endif

    private const string DeveloperId = "bb57d8e1-f911-47ba-b510-693be162686a";

#if ANDROID
    private const string AppKey = "MC4CFQC76uXj3J36NLgYLaZP7YevE/A4pgIVAPqOydqV4fv4Gh5v01DJGbaSbY61";
#elif WINDOWS
    private const string AppKey = "MCwCFAJIzwgOK1fYEE5KdmPOe+Lm+5x6AhR6kYJKPLvEsh8TO7jaivECRe5C9A==";
#else
    private const string AppKey = "";
#endif

    private CaptureHelper? _capture;
    private bool _isFirstLaunch = true;
    private readonly List<CaptureHelperDevice> _connectedDevices = [];

    public ScannerStatus Status { get; private set; } = ScannerStatus.Disconnected;
    public string ScannerName { get; private set; } = string.Empty;
    public string LastBarcode { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;

    public event EventHandler<string>? BarcodeScanned;
    public event EventHandler<ScannerStatus>? StatusChanged;

    public Task<IReadOnlyList<BluetoothDeviceInfo>> GetPairedDevicesAsync()
    {
        var list = _connectedDevices
            .Select(d => new BluetoothDeviceInfo(
                d.GetDeviceInfo().Guid,
                d.GetDeviceInfo().Name))
            .ToList();
        return Task.FromResult<IReadOnlyList<BluetoothDeviceInfo>>(list);
    }

    public Task ConnectAsync(string deviceAddress, string deviceName) => Task.CompletedTask;
    public Task DisconnectAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        // Close previous session if re-initializing
        if (_capture != null)
        {
            try { await _capture.CloseAsync(); } catch { }
            _capture = null;
            _isFirstLaunch = true;
            _connectedDevices.Clear();
        }

        _capture = new CaptureHelper();

        SetStatus(ScannerStatus.Connecting);
        LastError = string.Empty;

        long result = await _capture.OpenAsync(AppId, DeveloperId, AppKey);

        System.Diagnostics.Debug.WriteLine($"[CaptureSDK] OpenAsync returned {result}");

        if (SktErrors.SKTSUCCESS(result))
        {
            if (_isFirstLaunch)
            {
                _capture.DeviceArrival += OnDeviceArrival;
                _capture.DeviceRemoval += OnDeviceRemoval;
                _capture.DecodedData += OnDecodedData;
                _isFirstLaunch = false;

#if ANDROID
                try
                {
                    var ext = new CaptureExtension(
                        Android.App.Application.Context,
                        _capture.GetHandle());
                    ext.Start();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CaptureSDK] CaptureExtension error: {ex.Message}");
                }
#endif
            }

            // If no device already connected, stay Connecting until DeviceArrival
            if (_connectedDevices.Count == 0)
                SetStatus(ScannerStatus.Connecting);
        }
        else
        {
            LastError = $"Capture SDK error: {result}";
            SetStatus(ScannerStatus.Disconnected);
        }
    }

    public async Task CloseAsync()
    {
        if (_capture != null)
        {
            try { await _capture.CloseAsync(); } catch { }
            _capture = null;
        }
        _connectedDevices.Clear();
        SetStatus(ScannerStatus.Disconnected);
    }

    public void SimulateScan(string barcode)
    {
        LastBarcode = barcode;
        BarcodeScanned?.Invoke(this, barcode);
    }

    private void OnDeviceArrival(object? sender, CaptureHelper.DeviceArgs e)
    {
        var device = e.CaptureDevice;
        _connectedDevices.Add(device);
        ScannerName = device.GetDeviceInfo().Name;
        System.Diagnostics.Debug.WriteLine($"[CaptureSDK] Device arrived: {ScannerName}");
        SetStatus(ScannerStatus.Connected);
    }

    private void OnDeviceRemoval(object? sender, CaptureHelper.DeviceArgs e)
    {
        var name = e.CaptureDevice.GetDeviceInfo().Name;
        _connectedDevices.RemoveAll(d => d.GetDeviceInfo().Name == name);
        System.Diagnostics.Debug.WriteLine($"[CaptureSDK] Device removed: {name}");

        if (_connectedDevices.Count == 0)
        {
            ScannerName = string.Empty;
            SetStatus(ScannerStatus.Disconnected);
        }
        else
        {
            ScannerName = _connectedDevices[^1].GetDeviceInfo().Name;
        }
    }

    private void OnDecodedData(object? sender, CaptureHelper.DecodedDataArgs e)
    {
        var barcode = e.DecodedData.DataToUTF8String;
        System.Diagnostics.Debug.WriteLine($"[CaptureSDK] Barcode: {barcode}");
        LastBarcode = barcode;
        BarcodeScanned?.Invoke(this, barcode);
    }

    private void SetStatus(ScannerStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
