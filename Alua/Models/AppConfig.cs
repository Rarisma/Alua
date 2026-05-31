using Alua.Services.ViewModels;

namespace Alua.Models;
/// <summary>
/// Holds API keys sourced from user settings.
/// </summary>
public static class AppConfig
{
    /// <summary>
    /// Steam API Key.
    /// </summary>
    public static string? SteamAPIKey { get; set; }

    /// <summary>
    /// RetroAchievements API Key.
    /// </summary>
    public static string? RAAPIKey { get; set; }

    /// <summary>
    /// Copies API keys from the user's settings into the static config slots.
    /// </summary>
    public static void Refresh(SettingsVM settings)
    {
        SteamAPIKey = settings.UserSteamApiKey;
        RAAPIKey = settings.UserRetroApiKey;
    }
}
