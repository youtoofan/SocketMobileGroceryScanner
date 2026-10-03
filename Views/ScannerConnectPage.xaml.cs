using SocketMobileGroceryScanner.ViewModels;

namespace SocketMobileGroceryScanner.Views;

public partial class ScannerConnectPage : ContentPage
{
    private readonly ScannerConnectViewModel? _vm;

    public ScannerConnectPage(ScannerConnectViewModel vm)
    {
        try
        {
            InitializeComponent();
            _vm = vm;
            BindingContext = vm;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScannerConnectPage] Init failed: {ex}");
            Content = new Label { Text = $"Page error: {ex.Message}", Padding = 20 };
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (_vm != null)
                await _vm.LoadDevicesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScannerConnect] OnAppearing failed: {ex}");
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
