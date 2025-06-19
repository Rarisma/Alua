using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.DependencyInjection;

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
        SettingsVM svm = Ioc.Default.GetRequiredService<SettingsVM>();
        Providers = new();
        if (!string.IsNullOrWhiteSpace(svm.SteamID))
        {
            Providers.Add(await SteamService.Create(svm.SteamID));
        }
        if (!string.IsNullOrWhiteSpace(svm.RetroAchievementsUsername))
        {
            Providers.Add(await SteamService.Create(svm.RetroAchievementsUsername));
        }
    }
}
