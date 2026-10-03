using SocketMobileGroceryScanner.Services;

namespace SocketMobileGroceryScanner;

public partial class App : Application
{
    private readonly AppShell _shell;
    private const string CrashPrefKey = "last_crash";

    public App(AppShell shell)
    {
        InitializeComponent();
        _shell = shell;

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            SaveCrash(ex?.ToString() ?? "Unknown unhandled exception");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            SaveCrash(e.Exception?.ToString() ?? "Unknown task exception");
            e.SetObserved();
        };
    }

    private static void SaveCrash(string message)
    {
        try { Preferences.Set(CrashPrefKey, message); } catch { }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell);
        window.Created += async (s, e) =>
        {
            await Task.Delay(1000);

            // Show previous crash info
            var crash = Preferences.Get(CrashPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(crash))
            {
                Preferences.Remove(CrashPrefKey);
                if (_shell.CurrentPage != null)
                    await _shell.CurrentPage.DisplayAlert("Previous Crash", crash, "OK");
            }

            // Auto-connect to last known scanner
            try
            {
                var scanner = Handler?.MauiContext?.Services.GetService<IScannerService>();
                if (scanner != null)
                    await scanner.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Scanner auto-connect failed: {ex.Message}");
            }
        };
        return window;
    }
}