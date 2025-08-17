using System.Collections.ObjectModel;
using Alua.Models;
using Alua.Services;
using Alua.Services.Providers;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;

//Some things can never be fixed, they must be destroyed.
namespace Alua.Services.ViewModels;
/// <summary>
/// Main VM, Yeah it kinda breaks MVVM, but I don't care.
/// </summary>
public partial class AppVM : ObservableRecipient
{
    [ObservableProperty]
    private ObservableCollection<Game> _filteredGames = new();

    [ObservableProperty]
    private Game _selectedGame = new();

    [ObservableProperty]
    private string _loadingGamesSummary = string.Empty;

    [ObservableProperty] 
    private bool _initialLoadCompleted;

    // Filter properties to persist between page changes
    [ObservableProperty]
    private bool _hideComplete;

    [ObservableProperty]
    private bool _hideNoAchievements;

    [ObservableProperty]
    private bool _hideUnstarted;

    [ObservableProperty]
    private bool _reverse;

    [ObservableProperty]
    private OrderBy _orderBy = OrderBy.Name;

    [ObservableProperty]
    private bool _singleColumnLayout;

    [ObservableProperty]
    private Visibility _commandBarVisibility = Visibility.Visible;
    
    public List<IAchievementProvider> Providers = new();
    
    /// <summary>
    /// Adds or updates the Xbox provider when authentication changes
    /// </summary>
    public async Task<bool> ConfigureXboxProvider(string? microsoftAuthData)
    {
        try
        {
            // Remove existing Xbox provider if any
            var existingXbox = Providers.FirstOrDefault(p => p is XboxService);
            if (existingXbox != null)
            {
                Providers.Remove(existingXbox);
                Log.Information("Removed existing Xbox provider");
            }
            
            if (string.IsNullOrWhiteSpace(microsoftAuthData))
            {
                Log.Information("No Microsoft auth data provided, Xbox provider not configured");
                return false;
            }
            
            Log.Information("Configuring Xbox Live provider");
            
            // Create Microsoft auth service and restore authentication
            var msAuthService = new MicrosoftAuthService();
            var restored = await msAuthService.RestoreAuthDataAsync(microsoftAuthData);
            
            if (!restored)
            {
                Log.Warning("Failed to restore Xbox authentication from stored data");
                return false;
            }
            
            var accessToken = msAuthService.GetCachedAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                Log.Warning("Xbox authentication data exists but access token is expired or unavailable");
                return false;
            }
            
            var xbox = await XboxService.Create(accessToken);
            
            // Set up token refresh callback
            if (xbox != null && xbox._apiClient != null)
            {
                var svm = Ioc.Default.GetRequiredService<SettingsVM>();
                xbox._apiClient.SetTokenRefreshCallback(async () => 
                {
                    Log.Information("Refreshing Xbox Live access token");
                    var newToken = await msAuthService.RefreshAccessTokenAsync();
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        // Update stored auth data
                        svm.MicrosoftAuthData = await msAuthService.SerializeAuthDataAsync();
                        await svm.Save();
                    }
                    return newToken;
                });
            }
            
            Providers.Add(xbox);
            Log.Information("Successfully configured Xbox Live provider");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to configure Xbox provider");
            return false;
        }
    }
    
    /// <summary>
    /// Scans for Xbox games and adds them to the collection
    /// </summary>
    private async Task ScanXboxGamesAsync(IAchievementProvider xboxProvider)
    {
        try
        {
            Log.Information("Scanning for Xbox games");
            LoadingGamesSummary = "Loading Xbox games...";
            
            var svm = Ioc.Default.GetRequiredService<SettingsVM>();
            var games = await xboxProvider.GetLibrary();
            
            Log.Information("Found {Count} Xbox games", games.Length);
            
            foreach (var game in games)
            {
                svm.AddOrUpdateGame(game);
                
                // Add to filtered games if not already present
                if (!FilteredGames.Any(g => g.Identifier == game.Identifier))
                {
                    FilteredGames.Add(game);
                }
            }
            
            // Save the updated games list
            await svm.Save();
            
            LoadingGamesSummary = "";
            Log.Information("Xbox games scan complete. Total games in library: {Count}", svm.Games.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to scan Xbox games");
            LoadingGamesSummary = "";
        }
    }
    
    /// <summary>
    /// Adds or updates the Xbox provider using a fresh access token
    /// </summary>
    public async Task<bool> ConfigureXboxProviderWithToken(string accessToken, MicrosoftAuthService authService)
    {
        try
        {
            // Remove existing Xbox provider if any
            var existingXbox = Providers.FirstOrDefault(p => p is XboxService);
            if (existingXbox != null)
            {
                Providers.Remove(existingXbox);
                Log.Information("Removed existing Xbox provider");
            }
            
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                Log.Information("No access token provided, Xbox provider not configured");
                return false;
            }
            
            Log.Information("Configuring Xbox Live provider with access token");
            
            var xbox = await XboxService.Create(accessToken);
            
            // Set up token refresh callback
            if (xbox != null && xbox._apiClient != null)
            {
                var svm = Ioc.Default.GetRequiredService<SettingsVM>();
                xbox._apiClient.SetTokenRefreshCallback(async () => 
                {
                    Log.Information("Refreshing Xbox Live access token");
                    var newToken = await authService.RefreshAccessTokenAsync();
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        // Update stored auth data
                        svm.MicrosoftAuthData = await authService.SerializeAuthDataAsync();
                        await svm.Save();
                    }
                    return newToken;
                });
            }
            
            Providers.Add(xbox);
            Log.Information("Successfully configured Xbox Live provider with access token");
            
            // Don't automatically scan - let user trigger it manually
            
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to configure Xbox provider with token");
            return false;
        }
    }

    public async Task ConfigureProviders()
    {
        try
        {
            Log.Information("Configuring providers");
            SettingsVM svm = Ioc.Default.GetRequiredService<SettingsVM>();
            Providers = new();
            if (!string.IsNullOrWhiteSpace(svm.SteamID))
            {
                Log.Information("Configuring steam achievements");
                var steam = await SteamService.Create(svm.SteamID);
                Providers.Add(steam);
                Log.Information("Successfully configured steam achievements");
            }
            if (!string.IsNullOrWhiteSpace(svm.RetroAchievementsUsername))
            {
                Log.Information("Configuring retro achievements");
                var ra = await RetroAchievementsService.Create(svm.RetroAchievementsUsername);
                Providers.Add(ra);
                Log.Information("Successfully configured retro achievements");
            }
            if (!string.IsNullOrWhiteSpace(svm.PsnSSO))
            {
                Log.Information("Configuring PSN achievements");
                var psn = await PSNService.Create(svm.PsnSSO);
                Providers.Add(psn);
                Log.Information("Successfully configured PSN achievements");
            }
            
            if (!string.IsNullOrWhiteSpace(svm.MicrosoftAuthData))
            {
                Log.Information("Configuring Xbox Live achievements");
                
                // Create Microsoft auth service and restore authentication
                var msAuthService = new MicrosoftAuthService();
                var restored = await msAuthService.RestoreAuthDataAsync(svm.MicrosoftAuthData);
                
                if (restored)
                {
                    var accessToken = msAuthService.GetCachedAccessToken();
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        try
                        {
                            var xbox = await XboxService.Create(accessToken);
                            
                            // Set up token refresh callback
                            if (xbox != null && xbox._apiClient != null)
                            {
                                xbox._apiClient.SetTokenRefreshCallback(async () => 
                                {
                                    Log.Information("Refreshing Xbox Live access token");
                                    var newToken = await msAuthService.RefreshAccessTokenAsync();
                                    if (!string.IsNullOrEmpty(newToken))
                                    {
                                        // Update stored auth data
                                        svm.MicrosoftAuthData = await msAuthService.SerializeAuthDataAsync();
                                        await svm.Save();
                                    }
                                    return newToken;
                                });
                            }
                            
                            Providers.Add(xbox);
                            Log.Information("Successfully configured Xbox Live achievements with gamertag: {Gamertag}", svm.XboxGamertag);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to create Xbox service with restored token, clearing stored auth data");
                            // Clear invalid auth data so user can re-authenticate
                            svm.MicrosoftAuthData = null;
                            svm.XboxGamertag = null;
                            await svm.Save();
                        }
                    }
                    else
                    {
                        Log.Warning("Xbox authentication data exists but access token is expired or unavailable");
                        // Don't clear auth data here - user might just need to sign in again
                    }
                }
                else
                {
                    Log.Warning("Failed to restore Xbox authentication from stored data - token may be expired");
                    // Don't clear auth data - RestoreAuthDataAsync already tried silent refresh
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialise all providers successfully.");
        }

    }
    
}

public enum OrderBy { Name, CompletionPct, TotalCount, UnlockedCount, Playtime, LastUpdated, HowLongToBeatMain, HowLongToBeatCompletionist }
