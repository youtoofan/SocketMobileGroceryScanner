using Microsoft.Identity.Client;

namespace SocketMobileGroceryScanner.Services;

public class MsalAuthService
{
    // Register your app at https://portal.azure.com → App registrations
    // Audience: "consumers" (personal Microsoft accounts)
    // Android redirect URI: msal{ClientId}://auth
    // Windows redirect URI: http://localhost
    private const string ClientId = "cb8a627b-0974-40cd-8ced-aa5c8e6659c5";
    private static readonly string[] Scopes = ["Tasks.ReadWrite", "User.Read"];

    private readonly IPublicClientApplication _pca;

    public MsalAuthService()
    {
        var builder = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(AadAuthorityAudience.PersonalMicrosoftAccount);

#if ANDROID
        // Android uses the custom scheme registered in AndroidManifest.xml
        builder = builder.WithRedirectUri($"msal{ClientId}://auth");
#else
        // Desktop: use the well-known native-client redirect URI.
        // This is pre-authorized by Microsoft and requires no explicit
        // portal registration (avoids the "must include client_secret" error
        // that occurs when http://localhost is mis-registered under Web platform).
        builder = builder.WithDefaultRedirectUri();
#endif

        _pca = builder.Build();

        // Only register custom file-based cache on desktop.
        // Android/iOS have built-in MSAL token persistence.
#if WINDOWS
        RegisterTokenCache();
#endif
    }

    /// <summary>
    /// Persists the MSAL token cache to disk so users stay signed in across app restarts.
    /// </summary>
    private void RegisterTokenCache()
    {
        var cacheDir = Path.Combine(FileSystem.AppDataDirectory, "MSAL");
        var cacheFilePath = Path.Combine(cacheDir, "msal_token_cache.dat");

        _pca.UserTokenCache.SetBeforeAccessAsync(async args =>
        {
            try
            {
                if (File.Exists(cacheFilePath))
                {
                    var data = await File.ReadAllBytesAsync(cacheFilePath);
                    args.TokenCache.DeserializeMsalV3(data);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MSAL] Cache read failed: {ex.Message}");
            }
        });

        _pca.UserTokenCache.SetAfterAccessAsync(async args =>
        {
            try
            {
                if (args.HasStateChanged)
                {
                    Directory.CreateDirectory(cacheDir);
                    var data = args.TokenCache.SerializeMsalV3();
                    await File.WriteAllBytesAsync(cacheFilePath, data);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MSAL] Cache write failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Acquires a token silently from the cache. Returns null if no cached
    /// account exists or the refresh token has expired (no UI prompt).
    /// </summary>
    public async Task<AuthenticationResult?> AcquireTokenSilentAsync()
    {
        var accounts = await _pca.GetAccountsAsync();
        var account = accounts.FirstOrDefault();
        if (account == null) return null;

        try
        {
            return await _pca.AcquireTokenSilent(Scopes, account).ExecuteAsync();
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MSAL] Silent token failed: {ex.Message}");
            return null;
        }
    }

    public async Task<AuthenticationResult?> AcquireTokenAsync()
    {
        // Try silent first
        var result = await AcquireTokenSilentAsync();
        if (result != null) return result;

        // Interactive sign-in (platform-specific parent window)
        try
        {
            var interactive = _pca.AcquireTokenInteractive(Scopes);

#if ANDROID
            interactive = interactive.WithParentActivityOrWindow(Platform.CurrentActivity);
#elif WINDOWS
            var hwnd = ((MauiWinUIWindow)Application.Current!.Windows[0].Handler!.PlatformView!).WindowHandle;
            interactive = interactive.WithParentActivityOrWindow(hwnd);
#endif

            return await interactive.ExecuteAsync();
        }
        catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
        {
            return null;
        }
    }

    public async Task SignOutAsync()
    {
        var accounts = await _pca.GetAccountsAsync();
        foreach (var account in accounts)
            await _pca.RemoveAsync(account);
    }

    public async Task<string?> GetUserNameAsync()
    {
        var accounts = await _pca.GetAccountsAsync();
        return accounts.FirstOrDefault()?.Username;
    }

    public async Task<bool> IsSignedInAsync()
    {
        var accounts = await _pca.GetAccountsAsync();
        return accounts.Any();
    }
}
