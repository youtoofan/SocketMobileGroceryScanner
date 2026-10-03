namespace SocketMobileGroceryScanner.Services;

public enum ScannerStatus { Disconnected, Connecting, Connected }

public record BluetoothDeviceInfo(string Address, string Name);

public interface IScannerService
{
    ScannerStatus Status { get; }
    string ScannerName { get; }
    string LastBarcode { get; }
    string LastError { get; }
    event EventHandler<string> BarcodeScanned;
    event EventHandler<ScannerStatus> StatusChanged;
    Task<IReadOnlyList<BluetoothDeviceInfo>> GetPairedDevicesAsync();
    Task ConnectAsync(string deviceAddress, string deviceName);
    Task DisconnectAsync();
    Task InitializeAsync();
    Task CloseAsync();
    void SimulateScan(string barcode);
}
