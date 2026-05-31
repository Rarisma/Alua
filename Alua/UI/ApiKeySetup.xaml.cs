using System.Text.Json;
using System.Text.RegularExpressions;
using Alua.Models;
using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Alua.UI;

/// <summary>
/// Identifies which API key the setup page is configuring.
/// </summary>
public enum ApiKeyProvider
{
    Steam,
    RetroAchievements
}

/// <summary>
/// WebView-backed helper that walks the user through obtaining a Steam or
/// RetroAchievements Web API key, then stores it in settings.
/// </summary>
public sealed partial class ApiKeySetup : Page
{
    private const string SteamApiKeyUrl = "https://steamcommunity.com/dev/apikey";
    private const string RetroApiKeyUrl = "https://retroachievements.org/settings";

    private readonly SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    private ApiKeyProvider _provider = ApiKeyProvider.Steam;

    public ApiKeySetup()
    {
        InitializeComponent();
        SetupWebView.NavigationCompleted += OnNavigationCompleted;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ApiKeyProvider provider)
            _provider = provider;

        Configure();
    }

    private void Configure()
    {
        switch (_provider)
        {
            case ApiKeyProvider.Steam:
                TitleText.Text = "Set up Steam API key";
                InstructionsText.Text =
                    "Sign in to Steam below. Enter any domain name (e.g. \"localhost\") and agree to the terms, then copy the displayed key into the box.";
                ApiKeyBox.Text = _settingsVM.UserSteamApiKey ?? string.Empty;
                SetupWebView.Source = new Uri(SteamApiKeyUrl);
                break;
            case ApiKeyProvider.RetroAchievements:
                TitleText.Text = "Set up RetroAchievements API key";
                InstructionsText.Text =
                    "Sign in to RetroAchievements below. Scroll to the \"Keys\" section and copy your Web API Key into the box.";
                ApiKeyBox.Text = _settingsVM.UserRetroApiKey ?? string.Empty;
                SetupWebView.Source = new Uri(RetroApiKeyUrl);
                break;
        }
    }

    private async void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
            return;

        try
        {
            var extracted = await TryExtractKeyAsync();
            if (!string.IsNullOrEmpty(extracted) && string.IsNullOrWhiteSpace(ApiKeyBox.Text))
            {
                ApiKeyBox.Text = extracted;
                Log.Information("Auto-detected {Provider} API key from page", _provider);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Auto-extracting API key failed; user will paste manually");
        }
    }

    private async Task<string?> TryExtractKeyAsync()
    {
        // Pull the page body as text, then regex out the key. Both providers
        // render the key inline once the user is signed in, so this is enough
        // without coupling to brittle DOM selectors.
        var raw = await SetupWebView.ExecuteScriptAsync("document.body.innerText");
        if (string.IsNullOrEmpty(raw) || raw == "null")
            return null;

        var body = JsonSerializer.Deserialize<string>(raw);
        if (string.IsNullOrEmpty(body))
            return null;

        // Steam shows: "Key: XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX" (32 hex chars).
        // RA shows the Web API key as a 32+ char alphanumeric token in the Keys section.
        var match = Regex.Match(body, @"Key:\s*([A-Za-z0-9]{20,})");
        if (match.Success)
            return match.Groups[1].Value;

        if (_provider == ApiKeyProvider.RetroAchievements)
        {
            var raMatch = Regex.Match(body, @"Web API Key[^A-Za-z0-9]+([A-Za-z0-9]{20,})");
            if (raMatch.Success)
                return raMatch.Groups[1].Value;
        }

        return null;
    }

    private async Task OnSave()
    {
        try
        {
            Progress.IsActive = true;
            var key = ApiKeyBox.Text?.Trim();

            if (_provider == ApiKeyProvider.Steam)
                _settingsVM.UserSteamApiKey = string.IsNullOrEmpty(key) ? null : key;
            else
                _settingsVM.UserRetroApiKey = string.IsNullOrEmpty(key) ? null : key;

            AppConfig.Refresh(_settingsVM);
            await _settingsVM.Save();
            Log.Information("Saved {Provider} API key", _provider);
            GoBack();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save API key");
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Error",
                Content = $"Failed to save: {ex.Message}",
                PrimaryButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
        finally
        {
            Progress.IsActive = false;
        }
    }

    private void OnCancel() => GoBack();

    private void GoBack()
    {
        if (App.Frame.CanGoBack)
            App.Frame.GoBack();
    }
}
