using System.Collections.ObjectModel;
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
    private string _gamesFoundMessage = string.Empty;

    [ObservableProperty]
    private string _loadingGamesSummary = string.Empty;

}
