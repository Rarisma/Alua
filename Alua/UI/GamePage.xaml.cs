using Alua.Helpers;
using Alua.Services.Providers;
using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
using Microsoft.UI.Xaml.Input;
using AppVM = Alua.Services.ViewModels.AppVM;
using Timer = System.Timers.Timer;

//And guess what? It's not the pizza guy!
namespace Alua.UI;

public sealed partial class GamePage : Page
{
    AppVM AppVM = Ioc.Default.GetRequiredService<AppVM>();
    SettingsVM SettingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    private Timer _refreshTimer;


    private readonly BatchObservableCollection<Achievement> _filteredAchievements = new();
    public BatchObservableCollection<Achievement> FilteredAchievements => _filteredAchievements;

    private bool _showUnlocked = true, _showLocked = true, _hideHidden = true, _missableOnly = false;

    private readonly bool _isPhone = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    /// <summary>
    /// The edition whose achievements / stats are currently shown. For a merged game this is the
    /// selected tab; for a normal game it is just <see cref="AppVM.SelectedGame"/>. The header and
    /// achievement list bind to this; tab changes call <c>Bindings.Update()</c> to re-read it.
    /// </summary>
    private Game? _currentEdition;
    public Game CurrentEdition => _currentEdition ?? AppVM.SelectedGame;

    public GamePage()
    {
        InitializeComponent();

        _currentEdition = AppVM.SelectedGame;
        _refreshTimer = new Timer(60000);
        _refreshTimer.AutoReset = true;
        _refreshTimer.Elapsed += async (s, e) => await Refresh();
        _refreshTimer.Start();
        
        // Override scroll handling for faster trackpad scrolling on desktop
        if (!_isPhone)
            achievementsScrollViewer.AddHandler(PointerWheelChangedEvent,
                new PointerEventHandler(OnScrollViewerPointerWheelChanged), true);

        // Highlight the first edition tab once the list is realized (merged games only).
        if (AppVM.SelectedGame.IsMerged)
            Loaded += SelectFirstEditionOnLoad;

        RefreshFiltered();
    }

    private void SelectFirstEditionOnLoad(object sender, RoutedEventArgs e)
    {
        Loaded -= SelectFirstEditionOnLoad;
        if (EditionTabs.Items.Count > 0)
            EditionTabs.SelectedIndex = 0;
    }

    private void EditionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditionTabs.SelectedItem is not Game edition)
            return;

        _currentEdition = edition;
        // Header binds to CurrentEdition.* via OneWay compiled bindings; force them to re-read.
        Bindings.Update();
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
        var achievements = CurrentEdition?.Achievements ?? new();
        var showUnlocked = _showUnlocked;
        var showLocked = _showLocked;
        var hideHidden = _hideHidden;
        var missableOnly = _missableOnly;
        // Only RetroAchievements has a per-achievement discussion page, so the "Message" link is shown
        // only for RA editions. Re-evaluated on every rebuild, so switching edition tabs on a merged
        // game that spans platforms updates the links correctly.
        var isRetro = CurrentEdition?.Platform == Platforms.RetroAchievements;

        var filtered = await Task.Run(() =>
        {
            var list = achievements
                .Where(a => showUnlocked || !a.IsUnlocked)
                .Where(a => showLocked || a.IsUnlocked)
                .Where(a => !hideHidden || !a.IsHidden || a.IsUnlocked)
                .Where(a => !missableOnly || a.IsMissable)
                .ToList();
            foreach (var a in list)
                a.IsRA = isRetro;
            return list;
        });

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
        // Refresh the edition currently shown (the selected tab), not necessarily the group primary.
        var target = CurrentEdition;
        try
        {
            //Resolve game provider
            IAchievementProvider? provider = target.Platform switch
            {
                Platforms.Steam => AppVM.GetProvider<SteamService>(),
                Platforms.RetroAchievements => AppVM.GetProvider<RetroAchievementsService>(),
                Platforms.PlayStation => AppVM.GetProvider<PSNService>(),
                Platforms.Xbox => AppVM.GetProvider<XboxService>(),
                _ => null
            };

            if (provider == null)
            {
                Log.Warning("No provider available for platform {Platform}", target.Platform);
                return;
            }

            //Update settings collection and this page's binding source.
            Game game = await provider.RefreshTitle(target.Identifier);
            SettingsVM.AddOrUpdateGame(game);

            if (AppVM.SelectedGame.IsMerged)
            {
                // Splice the refreshed edition back into the open group so the page reflects the
                // new data, without collapsing the merge (SelectedGame stays the representative,
                // keeping its Editions list and group display name). Re-grouping on the next
                // library refresh reconciles everything from the persisted dictionary.
                var editions = AppVM.SelectedGame.Editions;
                var idx = editions.FindIndex(e => e.Identifier == game.Identifier);
                if (idx >= 0)
                    editions[idx] = game;
                _currentEdition = game;
            }
            else
            {
                AppVM.SelectedGame = game;
                _currentEdition = game;
            }

            // Force x:Bind compiled bindings rooted at this page (CurrentEdition.*) to pick up the
            // new edition reference. Bindings.Update() also covers any OneTime bindings that remain.
            DispatcherQueue.TryEnqueue(() => Bindings.Update());

            await SettingsVM.Save();
            RefreshFiltered();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cannot refresh game ID {GameId}", target.Identifier);
        }
    }

    private async void OpenRAConvoClick(object sender, RoutedEventArgs e)
    {
        // Tag carries the achievement Id; open its RetroAchievements page in the default browser.
        // Launcher.LaunchUriAsync works across desktop and mobile, unlike Process.Start which throws
        // on .NET when handed a URL (UseShellExecute defaults to false).
        if ((sender as FrameworkElement)?.Tag is not { } id)
            return;

        var uri = new Uri($"https://retroachievements.org/achievement/{id}");
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open RetroAchievements link {Uri}", uri);
        }
    }
}
