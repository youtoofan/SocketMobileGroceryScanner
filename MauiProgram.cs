using CommunityToolkit.Maui;
using SocketMobileGroceryScanner.Data;
using SocketMobileGroceryScanner.Services;
using SocketMobileGroceryScanner.ViewModels;
using SocketMobileGroceryScanner.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SocketMobileGroceryScanner;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Database
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "groceryscanner.db");
        builder.Services.AddDbContextFactory<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"));

        // HTTP with resilience
        builder.Services.AddHttpClient("ProductApi", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "GroceryScanner/1.0 (+https://github.com/example/grocery-scanner)");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddStandardResilienceHandler();

        // Free, keyless translation (MyMemory) and image fallback (Wikipedia) APIs
        builder.Services.AddHttpClient("TranslateApi", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "GroceryScanner/1.0 (+https://github.com/example/grocery-scanner)");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler();

        builder.Services.AddHttpClient("WikiApi", client =>
        {
            // NOTE: avoid putting an email address in this header — some corporate
            // network proxies/DLP gateways block outbound requests whose headers
            // contain an email-like pattern, which silently 403s every Wikimedia
            // image download. A URL-based contact string is treated as safe.
            client.DefaultRequestHeaders.Add("User-Agent", "GroceryScanner/1.0 (+https://github.com/example/grocery-scanner)");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler();

        // Services
        builder.Services.AddSingleton<IScannerService, SocketMobileScannerService>();
        builder.Services.AddSingleton<MsalAuthService>();
        builder.Services.AddSingleton<ITranslationService, TranslationService>();
        builder.Services.AddSingleton<IImageFallbackService, WikipediaImageFallbackService>();
        builder.Services.AddSingleton<IProductLookupService, ProductLookupService>();
        builder.Services.AddSingleton<IInventoryService, InventoryService>();
        builder.Services.AddSingleton<IShoppingListService, ShoppingListService>();
        builder.Services.AddSingleton<IMicrosoftToDoService, MicrosoftToDoService>();

        // ViewModels
        builder.Services.AddTransient<InventoryViewModel>();
        builder.Services.AddTransient<ShoppingListViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<ScannerConnectViewModel>();

        // Pages
        builder.Services.AddTransient<InventoryPage>();
        builder.Services.AddTransient<ShoppingListPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ScannerConnectPage>();

        // Shell
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Ensure DB is created + WAL mode
        using (var db = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

            // EnsureCreated doesn't alter already-existing tables, so manually add
            // columns introduced after the initial schema. Safe to ignore failures
            // caused by the column already existing.
            try
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE ShoppingListItems ADD COLUMN ToDoTaskId TEXT NULL");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] ToDoTaskId column migration skipped: {ex.Message}");
            }

            try
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE Products ADD COLUMN Lang TEXT NULL");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] Lang column migration skipped: {ex.Message}");
            }
        }

        // One-time, no-network repair of any image URLs that a network proxy mangled
        // in transit before this fix existed (see WikipediaImageFallbackService).
        try
        {
            var productLookup = app.Services.GetRequiredService<IProductLookupService>();
            _ = productLookup.RepairBrokenImageUrlsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DB] Broken image URL repair skipped: {ex.Message}");
        }

        // Force-resolve all singletons to surface DI errors early
        try
        {
            _ = app.Services.GetRequiredService<IScannerService>();
            _ = app.Services.GetRequiredService<MsalAuthService>();
            _ = app.Services.GetRequiredService<IProductLookupService>();
            _ = app.Services.GetRequiredService<IInventoryService>();
            _ = app.Services.GetRequiredService<IShoppingListService>();
            _ = app.Services.GetRequiredService<IMicrosoftToDoService>();
            System.Diagnostics.Debug.WriteLine("[DI] All singletons resolved OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DI] Singleton resolution FAILED: {ex}");
            var crashPath = Path.Combine(FileSystem.AppDataDirectory, "di_crash.log");
            File.WriteAllText(crashPath, ex.ToString());
        }

        return app;
    }
}
