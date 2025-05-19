using System.Collections.ObjectModel;
//Some things can never be fixed, they must be destroyed.
namespace Alua.Services;
/// <summary>
/// Main VM.
/// </summary>
public class AppVM : ObservableRecipient
{
    /// <summary>
    /// Shown in settings
    /// </summary>
    private const string BuildNumber = "0.1.0";

    /// <summary>
    /// Shown under build number, enables debug mode.
    /// </summary>
    private const string BuildString = "Excellent, Excited";

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
