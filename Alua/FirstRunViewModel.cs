using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Alua;

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
    
    
    public FirstRunVM()
    {
        _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
        // Initialize from SettingsVM if needed, though for a true first run, these would be null/empty.
        SteamID = _settingsVM.SteamID;
        RetroAchievementsUser = _settingsVM.RetroAchivementsUsername;
    }

    public void Continue()
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
        _settingsVM.RetroAchivementsUsername = RetroAchievementsUser;
        _settingsVM.Initialised = false; // Mark first run as complete

        App.Frame.Navigate(typeof(GameList));
    }

    partial void OnSteamIDChanged(string? value) => ClearErrorOnChange();
    partial void OnRetroAchievementsUserChanged(string? value) => ClearErrorOnChange();

    private void ClearErrorOnChange()
    {
        if (HasError)
        {
            HasError = false;
            ErrorMessage = null;
        }
    }
}
