using SocketMobileGroceryScanner.ViewModels;

namespace SocketMobileGroceryScanner.Views;

public partial class ShoppingListPage : ContentPage
{
    private ShoppingListViewModel? _vm;

    public ShoppingListPage(ShoppingListViewModel vm)
    {
        try
        {
            InitializeComponent();
            _vm = vm;
            BindingContext = vm;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShoppingListPage] Init failed: {ex}");
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
            System.Diagnostics.Debug.WriteLine($"[ShoppingList] OnAppearing failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm?.UnsubscribeFromScanner();
    }
}
