using System.Collections.ObjectModel;
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
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialise all providers successfully.");
        }

    }
}

public enum OrderBy { Name, CompletionPct, TotalCount, UnlockedCount, Playtime, LastUpdated }
