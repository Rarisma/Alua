using Alua.UI;

namespace Alua.Services.ViewModels;

public partial class FirstRunVM : ObservableObject
{
    private readonly SettingsVM _settingsVM;

    [ObservableProperty]
    private string? _steamID;

    [ObservableProperty]
    private string? _retroAchievementsUser;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;
    
    public FirstRunVM(SettingsVM settingsVM)
    {
        _settingsVM = settingsVM;
        SteamID = _settingsVM.SteamID;
        RetroAchievementsUser = _settingsVM.RetroAchievementsUsername;
    }
    
    /// <summary>
    /// Continue to the main UI.
    /// </summary>
    public async Task Continue()
    {
        if (string.IsNullOrWhiteSpace(SteamID) && string.IsNullOrWhiteSpace(RetroAchievementsUser))
        {
            ErrorMessage = "Please enter at least one ID or username.";
            HasError = true;
            return;
        }

        HasError = false;
        ErrorMessage = null;

        _settingsVM.SteamID = SteamID;
        _settingsVM.RetroAchievementsUsername = RetroAchievementsUser;
        _settingsVM.Initialised = true; // Mark first run as complete
        await _settingsVM.Save();
        
        App.Frame.Navigate(typeof(GameList));
    }

    partial void OnSteamIDChanged(string? value) => ClearErrorOnChange();
    partial void OnRetroAchievementsUserChanged(string? value) => ClearErrorOnChange();
    
    /// <summary>
    /// Resets error messages when a field is changed.
    /// </summary>
    private void ClearErrorOnChange()
    {
        if (HasError)
        {
            HasError = false;
            ErrorMessage = null;
        }
    }
}
