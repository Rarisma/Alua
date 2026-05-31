using Alua.Helpers;
using Alua.Services.Providers;
using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
using Microsoft.UI.Xaml.Input;
using AppVM = Alua.Services.ViewModels.AppVM;

//And guess what? It's not the pizza guy!
namespace Alua.UI;

public sealed partial class GamePage : Page
{
    AppVM AppVM = Ioc.Default.GetRequiredService<AppVM>();
    SettingsVM SettingsVM = Ioc.Default.GetRequiredService<SettingsVM>();

    private readonly BatchObservableCollection<Achievement> _filteredAchievements = new();
    public BatchObservableCollection<Achievement> FilteredAchievements => _filteredAchievements;

    private bool _showUnlocked = true, _showLocked = true, _hideHidden = true, _missableOnly = false;

    private readonly bool _isPhone = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    public GamePage()
    {
        InitializeComponent();

        // Override scroll handling for faster trackpad scrolling on desktop
        if (!_isPhone)
            achievementsScrollViewer.AddHandler(UIElement.PointerWheelChangedEvent,
                new PointerEventHandler(OnScrollViewerPointerWheelChanged), true);

        RefreshFiltered();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _showUnlocked = CheckShowUnlocked.IsChecked == true;
        _showLocked = CheckShowLocked.IsChecked == true;
        _hideHidden = CheckHideHidden.IsChecked == true;
        _missableOnly = CheckMissableOnly.IsChecked == true;
        RefreshFiltered();
    }

    private async void RefreshFiltered()
    {
        var achievements = AppVM.SelectedGame?.Achievements ?? new();
        var showUnlocked = _showUnlocked;
        var showLocked = _showLocked;
        var hideHidden = _hideHidden;
        var missableOnly = _missableOnly;

        var filtered = await Task.Run(() =>
            achievements
                .Where(a => showUnlocked || !a.IsUnlocked)
                .Where(a => showLocked || a.IsUnlocked)
                .Where(a => !hideHidden || !a.IsHidden || a.IsUnlocked)
                .Where(a => !missableOnly || a.IsMissable)
                .ToList()
        );

        _filteredAchievements.ReplaceAll(filtered);
    }
    
    private void OnScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        var delta = e.GetCurrentPoint(sv).Properties.MouseWheelDelta;
        sv.ChangeView(null, sv.VerticalOffset - delta, null, true);
        e.Handled = true;
    }

    /// <summary>
    /// Refreshes game data for this game
    /// </summary>
    /// <exception cref="NotImplementedException">Requested game provider is not available or doesn't exist.</exception>
    private async Task Refresh()
    {
        try
        {
            //Resolve game provider
            IAchievementProvider? provider = AppVM.SelectedGame.Platform switch
            {
                Platforms.Steam => AppVM.GetProvider<SteamService>(),
                Platforms.RetroAchievements => AppVM.GetProvider<RetroAchievementsService>(),
                Platforms.PlayStation => AppVM.GetProvider<PSNService>(),
                Platforms.Xbox => AppVM.GetProvider<XboxService>(),
                _ => null
            };

            if (provider == null)
            {
                Log.Warning("No provider available for platform {Platform}", AppVM.SelectedGame.Platform);
                return;
            }

            //Update settings collection and this page's binding source.
            Game game = await provider.RefreshTitle(AppVM.SelectedGame.Identifier);
            SettingsVM.AddOrUpdateGame(game);
            AppVM.SelectedGame = game;

            // Force x:Bind compiled bindings rooted at this page to pick up the new SelectedGame
            // reference. The OneWay bindings on AppVM.SelectedGame.* update automatically when
            // SelectedGame raises PropertyChanged, but Bindings.Update() is a belt-and-suspenders
            // guard for any OneTime bindings that may remain (e.g. the header icon).
            DispatcherQueue.TryEnqueue(() => Bindings.Update());

            await SettingsVM.Save();
            RefreshFiltered();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cannot refresh game ID {GameId}", AppVM.SelectedGame.Identifier);
        }
    }
}
