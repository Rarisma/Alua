using Microsoft.Identity.Client;
using Serilog;
using System.Text.Json;

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
    
    public MicrosoftAuthService()
    {
        _msalClient = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(Authority)
            .WithRedirectUri(RedirectUri)
            .Build();
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
                    _cachedResult = await _msalClient
                        .AcquireTokenSilent(Scopes, account)
                        .ExecuteAsync();
                    
                    Log.Information("Silent authentication successful");
                    return _cachedResult;
                }
                catch (MsalUiRequiredException)
                {
                    Log.Information("Silent authentication failed, user interaction required");
                    // Fall through to interactive login
                }
            }
            
            // Perform interactive authentication
            Log.Information("Starting interactive Microsoft authentication");
            
            _cachedResult = await _msalClient
                .AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false) // Use system browser for better compatibility
                .ExecuteAsync();
            
            Log.Information("Interactive authentication successful. Account: {Account}", 
                _cachedResult.Account?.Username ?? "Unknown");
            
            return _cachedResult;
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
            
            _cachedResult = await _msalClient
                .AcquireTokenSilent(Scopes, account)
                .ExecuteAsync();
            
            Log.Information("Token refresh successful");
            return _cachedResult.AccessToken;
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
            
            _cachedResult = null;
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
        if (_cachedResult == null || _cachedResult.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return null;
        }
        
        return _cachedResult.AccessToken;
    }
    
    /// <summary>
    /// Serializes the authentication data for storage
    /// </summary>
    public async Task<string?> SerializeAuthDataAsync()
    {
        try
        {
            var account = await GetCurrentAccountAsync();
            if (account == null || _cachedResult == null)
            {
                return null;
            }
            
            var authData = new SerializedAuthData
            {
                Username = account.Username,
                HomeAccountId = account.HomeAccountId?.Identifier,
                Environment = account.Environment,
                AccessToken = _cachedResult.AccessToken,
                ExpiresOn = _cachedResult.ExpiresOn,
                TenantId = _cachedResult.TenantId,
                UniqueId = _cachedResult.UniqueId,
                Account = _cachedResult.Account?.Username
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
                    _cachedResult = await _msalClient
                        .AcquireTokenSilent(Scopes, account)
                        .ExecuteAsync();
                    
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