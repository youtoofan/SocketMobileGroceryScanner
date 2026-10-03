using SocketMobileGroceryScanner.ViewModels;

namespace SocketMobileGroceryScanner.Views;

public partial class InventoryPage : ContentPage
{
    private InventoryViewModel? _vm;

    public InventoryPage(InventoryViewModel vm)
    {
        try
        {
            InitializeComponent();
            _vm = vm;
            BindingContext = vm;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InventoryPage] Init failed: {ex}");
            Content = new Label { Text = $"Page error: {ex.Message}", Padding = 20 };
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _vm?.SubscribeToScanner();
            if (_vm != null) await _vm.LoadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Inventory] OnAppearing failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm?.UnsubscribeFromScanner();
    }
}
