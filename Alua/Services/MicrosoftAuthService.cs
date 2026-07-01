using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensibility;
using Serilog;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace Alua.Services;

/// <summary>
/// Handles Microsoft OAuth authentication for Xbox Live access
/// </summary>
public class MicrosoftAuthService
{
    private const string ClientId = "d0779e70-74ed-419d-ac4e-b5cd96e664ca";
    private const string Authority = "https://login.microsoftonline.com/consumers";
    private const string RedirectUri = "http://localhost";

    // Xbox Live scopes required for authentication
    private static readonly string[] Scopes = new[]
    {
        "XboxLive.signin",
        "XboxLive.offline_access"
    };

    private readonly IPublicClientApplication _msalClient;
    private AuthenticationResult? _cachedResult;
    // Protects _cachedResult from concurrent read/write by background token-refresh and UI callers.
    private readonly object _cachedResultLock = new();
    private static readonly object TokenCacheLock = new();

    public MicrosoftAuthService()
    {
        _msalClient = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(Authority)
            .WithRedirectUri(RedirectUri)
            .Build();

        // MSAL handles token caching internally on mobile platforms;
        // custom file-based cache is only for desktop.
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
            ConfigureTokenCache(_msalClient.UserTokenCache);
    }
    
    /// <summary>
    /// Gets the currently authenticated account, if any
    /// </summary>
    public async Task<IAccount?> GetCurrentAccountAsync()
    {
        try
        {
            var accounts = await _msalClient.GetAccountsAsync();
            return accounts.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get current account");
            return null;
        }
    }
    
    /// <summary>
    /// Authenticates the user with Microsoft and returns tokens for Xbox Live
    /// </summary>
    /// <returns>Authentication result with access token and refresh token</returns>
    public async Task<AuthenticationResult?> AuthenticateAsync()
    {
        try
        {
            // First try to get a token silently if we have a cached account
            var account = await GetCurrentAccountAsync();
            if (account != null)
            {
                try
                {
                    Log.Information("Attempting silent authentication for cached account");
                    var silentResult = await _msalClient
                        .AcquireTokenSilent(Scopes, account)
                        .ExecuteAsync();
                    lock (_cachedResultLock) { _cachedResult = silentResult; }
                    Log.Information("Silent authentication successful");
                    return silentResult;
                }
                catch (MsalUiRequiredException)
                {
                    Log.Information("Silent authentication failed, user interaction required");
                    // Fall through to interactive login
                }
            }

            // Perform interactive authentication
            Log.Information("Starting interactive Microsoft authentication");

            var interactiveBuilder = _msalClient.AcquireTokenInteractive(Scopes);

            // Windows/macOS desktop: host sign-in in an integrated in-app WebView
            // (WebViewCustomWebUi). Linux keeps the system browser — Uno's X11 WebView is
            // unreliable under XWayland — and Android uses MSAL's native browser/custom-tabs flow.
            interactiveBuilder = UseIntegratedWebView()
                ? interactiveBuilder.WithCustomWebUi(new WebViewCustomWebUi())
                : interactiveBuilder.WithUseEmbeddedWebView(false);

            var interactiveResult = await interactiveBuilder.ExecuteAsync();
            lock (_cachedResultLock) { _cachedResult = interactiveResult; }

            Log.Information("Interactive authentication successful. Account: {Account}",
                interactiveResult.Account?.Username ?? "Unknown");

            return interactiveResult;
        }
        catch (MsalException msalEx)
        {
            Log.Error(msalEx, "MSAL authentication failed. Error code: {ErrorCode}", msalEx.ErrorCode);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during authentication");
            return null;
        }
    }

    /// <summary>
    /// The integrated in-app WebView is used only where Uno's WebView2 renders reliably:
    /// Windows and macOS desktop. Linux (XWayland) and Android fall back to their prior flows.
    /// </summary>
    private static bool UseIntegratedWebView()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
    
    /// <summary>
    /// Refreshes the access token using the refresh token
    /// </summary>
    public async Task<string?> RefreshAccessTokenAsync()
    {
        try
        {
            var account = await GetCurrentAccountAsync();
            if (account == null)
            {
                Log.Warning("No cached account found for token refresh");
                return null;
            }

            Log.Information("Refreshing access token for account: {Account}", account.Username);

            var refreshedResult = await _msalClient
                .AcquireTokenSilent(Scopes, account)
                .ExecuteAsync();
            lock (_cachedResultLock) { _cachedResult = refreshedResult; }

            Log.Information("Token refresh successful");
            return refreshedResult.AccessToken;
        }
        catch (MsalException msalEx)
        {
            Log.Error(msalEx, "Token refresh failed. Error code: {ErrorCode}", msalEx.ErrorCode);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during token refresh");
            return null;
        }
    }
    
    /// <summary>
    /// Signs out the current user and clears the token cache
    /// </summary>
    public async Task SignOutAsync()
    {
        try
        {
            var accounts = await _msalClient.GetAccountsAsync();
            foreach (var account in accounts)
            {
                await _msalClient.RemoveAsync(account);
                Log.Information("Signed out account: {Account}", account.Username);
            }

            lock (_cachedResultLock) { _cachedResult = null; }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during sign out");
        }
    }

    /// <summary>
    /// Gets the current access token if available
    /// </summary>
    public string? GetCachedAccessToken()
    {
        lock (_cachedResultLock)
        {
            if (_cachedResult == null || _cachedResult.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
                return null;
            return _cachedResult.AccessToken;
        }
    }
    
    /// <summary>
    /// Serializes the authentication data for storage
    /// </summary>
    public async Task<string?> SerializeAuthDataAsync()
    {
        try
        {
            var account = await GetCurrentAccountAsync();
            AuthenticationResult? snapshot;
            lock (_cachedResultLock) { snapshot = _cachedResult; }

            if (account == null || snapshot == null)
                return null;

            var authData = new SerializedAuthData
            {
                Username = account.Username,
                HomeAccountId = account.HomeAccountId?.Identifier,
                Environment = account.Environment,
                AccessToken = snapshot.AccessToken,
                ExpiresOn = snapshot.ExpiresOn,
                TenantId = snapshot.TenantId,
                UniqueId = snapshot.UniqueId,
                Account = snapshot.Account?.Username
            };

            return JsonSerializer.Serialize(authData);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to serialize auth data");
            return null;
        }
    }
    
    /// <summary>
    /// Attempts to restore authentication from serialized data
    /// </summary>
    public async Task<bool> RestoreAuthDataAsync(string serializedData)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(serializedData))
            {
                return false;
            }
            
            var authData = JsonSerializer.Deserialize<SerializedAuthData>(serializedData);
            if (authData == null)
            {
                return false;
            }
            
            // Try to get the account and refresh the token
            var accounts = await _msalClient.GetAccountsAsync();
            var account = accounts.FirstOrDefault(a => 
                a.HomeAccountId?.Identifier == authData.HomeAccountId ||
                a.Username == authData.Username);
            
            if (account != null)
            {
                try
                {
                    var restored = await _msalClient
                        .AcquireTokenSilent(Scopes, account)
                        .ExecuteAsync();
                    lock (_cachedResultLock) { _cachedResult = restored; }
                    Log.Information("Successfully restored authentication for {Account}", account.Username);
                    return true;
                }
                catch (MsalUiRequiredException)
                {
                    Log.Information("Stored authentication expired, user needs to re-authenticate");
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore auth data");
            return false;
        }
    }

    private static void ConfigureTokenCache(ITokenCache tokenCache)
    {
        tokenCache.SetBeforeAccess(OnBeforeAccessTokenCache);
        tokenCache.SetAfterAccess(OnAfterAccessTokenCache);
    }

    private const string MsalCacheSecretKey = "msal_cache_v1";

    private static void OnBeforeAccessTokenCache(TokenCacheNotificationArgs args)
    {
        lock (TokenCacheLock)
        {
            try
            {
                // Run on the thread pool so the WaitAsync continuation inside SecureStorage does
                // not deadlock when this MSAL callback runs synchronously on the UI thread.
                var encoded = Task.Run(() => SecureStorage.GetAsync(MsalCacheSecretKey)).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(encoded))
                    return;
                var data = Convert.FromBase64String(encoded);
                if (data.Length == 0)
                    return;
                args.TokenCache.DeserializeMsalV3(data, true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load Microsoft authentication token cache");
                args.TokenCache.DeserializeMsalV3(Array.Empty<byte>(), true);
            }
        }
    }

    private static void OnAfterAccessTokenCache(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged)
        {
            return;
        }

        lock (TokenCacheLock)
        {
            try
            {
                byte[] data = args.TokenCache.SerializeMsalV3();
                var encoded = data.Length == 0 ? string.Empty : Convert.ToBase64String(data);
                // Run on the thread pool so the WaitAsync continuation inside SecureStorage does
                // not deadlock when this MSAL callback runs synchronously on the UI thread.
                Task.Run(() => SecureStorage.SetAsync(MsalCacheSecretKey, encoded)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to persist Microsoft authentication token cache");
            }
        }
    }

    private class SerializedAuthData
    {
        public string? Username { get; set; }
        public string? HomeAccountId { get; set; }
        public string? Environment { get; set; }
        public string? AccessToken { get; set; }
        public DateTimeOffset ExpiresOn { get; set; }
        public string? TenantId { get; set; }
        public string? UniqueId { get; set; }
        public string? Account { get; set; }
    }
}
