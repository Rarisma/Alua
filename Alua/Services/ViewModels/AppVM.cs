using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;

//Some things can never be fixed, they must be destroyed.
namespace Alua.Services;
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
