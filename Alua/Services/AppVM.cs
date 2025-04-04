using System.Collections.ObjectModel;
//Some things can never be fixed, they must be destroyed.
namespace Alua.Services;
/// <summary>
/// Main VM.
/// </summary>
public class AppVM  : ObservableRecipient
{
    /// <summary>
    /// Shown in settings
    /// </summary>
    private const string BuildNumber = "0.0.2";
    
    /// <summary>
    /// Shown under build number, enables debug mode.
    /// </summary>
    private const string BuildString = "INVISIBLE";


    private Game _selectedGame;
    public Game SelectedGame
    {
        get => _selectedGame;
        set => SetProperty(ref _selectedGame, value);
    }
}
