using System.Collections.ObjectModel;
//Some things can never be fixed, they must be destroyed.
namespace Alua.Services;
/// <summary>
/// Main VM, Yeah it kinda breaks MVVM, but I don't care.
/// </summary>
public class AppVM : ObservableRecipient
{
    private ObservableCollection<Game> _filteredGames = new();
    public ObservableCollection<Game> FilteredGames
    {
        get => _filteredGames;
        set => SetProperty(ref _filteredGames, value);
    }

    private Game _selectedGame = new();
    public Game SelectedGame
    {
        get => _selectedGame;
        set => SetProperty(ref _selectedGame, value);
    }

    private string _gamesFoundMessage = string.Empty;
    public string GamesFoundMessage
    {
        get => _gamesFoundMessage;
        set => SetProperty(ref _gamesFoundMessage, value);
    }
    

    private string _loadingGamesSummary = string.Empty;
    public string LoadingGamesSummary
    {
        get => _loadingGamesSummary;
        set => SetProperty(ref _loadingGamesSummary, value);

    }
}
