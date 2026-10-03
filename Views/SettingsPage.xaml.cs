using SocketMobileGroceryScanner.ViewModels;

namespace SocketMobileGroceryScanner.Views;

public partial class SettingsPage : ContentPage
{
    private SettingsViewModel? _vm;

    public SettingsPage(SettingsViewModel vm)
    {
        try
        {
            InitializeComponent();
            _vm = vm;
            BindingContext = vm;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsPage] Init failed: {ex}");
            Content = new Label { Text = $"Page error: {ex.Message}", Padding = 20 };
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (_vm != null) await _vm.LoadCacheInfoAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] OnAppearing failed: {ex}");
        }
    }
}
