using SocketMobileGroceryScanner.Views;

namespace SocketMobileGroceryScanner;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("scannerconnect", typeof(ScannerConnectPage));
    }
}
