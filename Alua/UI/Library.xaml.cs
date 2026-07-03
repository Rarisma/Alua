using Alua.Models;
using Alua.Services;
using Alua.Services.ViewModels;
using Alua.UI.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
using Microsoft.UI.Xaml.Input;
using AppVM = Alua.Services.ViewModels.AppVM;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;
using static Alua.Services.ViewModels.OrderBy;

//FHN walked so Alua could run.
namespace Alua.UI;

/// <summary>
/// Main app UI, shows all users games
/// </summary>
public partial class Library : Page
{
    private AppVM _appVm = Ioc.Default.GetRequiredService<AppVM>();
    private SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    private readonly bool _isPhone = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    public LibraryVM LibraryVM = Ioc.Default.GetRequiredService<LibraryVM>();

    // Layouts for ItemsRepeater - swappable for single/multi column
    private readonly StackLayout _listLayout = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 8
    };

    private readonly UniformGridLayout _gridLayout = new()
    {
        Orientation = Orientation.Horizontal,
        MinItemWidth = 300,
        MinItemHeight = 130,
        MinRowSpacing = 8,
        MinColumnSpacing = 8,
        ItemsStretch = UniformGridLayoutItemsStretch.Fill,
        ItemsJustification = UniformGridLayoutItemsJustification.Start,
        MaximumRowsOrColumns = -1 // Unlimited columns based on available width
    };

    // Cancellation support for long-running operations
    private CancellationTokenSource? _operationCts;

    // Guards the one-time PointerWheelChanged subscription. Uno's LoadingView re-parents this
    // page's content when it toggles loading/loaded, so OnLoaded fires more than once per page
    // instance; without this we would stack a new handler every time.
    private bool _scrollHandlerHooked;

    // Commands for layouts — cached to avoid allocating a new instance on every read
    private AsyncCommand? _singleColumnCommand;
    public AsyncCommand SingleColumnCommand => _singleColumnCommand ??= new AsyncCommand(async () =>
    {
        _appVm.SingleColumnLayout = true;
        _settingsVM.SingleColumnLayout = true;
        UpdateItemsLayout();
        await _settingsVM.Save();
    });

    private AsyncCommand? _multiColumnCommand;
    public AsyncCommand MultiColumnCommand => _multiColumnCommand ??= new AsyncCommand(async () =>
    {
        if (_isPhone)
        {
            _appVm.SingleColumnLayout = true;
            _settingsVM.SingleColumnLayout = true;
            UpdateItemsLayout();
            return;
        }
        _appVm.SingleColumnLayout = false;
        _settingsVM.SingleColumnLayout = false;
        UpdateItemsLayout();
        await _settingsVM.Save();
    });

    public Library()
    {
        InitializeComponent();

        // Expose the SettingsVM to this page's resource scope so the card DataTemplates can bind
        // appearance preferences via {Binding ..., Source={StaticResource Settings}}. Page-scoped
        // (not App-level) because Uno does not resolve App resources from inside page DataTemplates.
        Resources["Settings"] = _settingsVM;

        // Seed the ItemsRepeater's layout and ItemTemplate BEFORE it realizes any items.
        // OnLoaded (where these previously ran first) fires only AFTER the initial render, so
        // on re-navigation — when FilteredGames is already populated by the AppVM singleton —
        // the repeater would realize items with a null ItemTemplate and fall back to ToString(),
        // rendering every card as the raw type name "Alua.Models.Game". Reading the persisted
        // values here lets items materialize with the correct template from the first layout pass.
        _appVm.SingleColumnLayout = _isPhone || _settingsVM.SingleColumnLayout;
        _appVm.CardProgressStyle = _settingsVM.CardProgressStyle;
        UpdateItemsLayout();
        UpdateFillMode();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Override scroll handling for faster trackpad scrolling on desktop. Guarded because
            // OnLoaded fires repeatedly (LoadingView re-parents the content on each loading toggle);
            // re-adding the handler would leak and multiply wheel handling.
            if (!_isPhone && !_scrollHandlerHooked)
            {
                gamesScrollViewer.AddHandler(PointerWheelChangedEvent,
                    new PointerEventHandler(OnScrollViewerPointerWheelChanged), true);
                _scrollHandlerHooked = true;
            }

            Log.Information("Initialised games list");
            _appVm.CommandBarVisibility = Visibility.Visible;

            // Load persisted filter settings into the app VM
            _appVm.HideComplete = _settingsVM.HideComplete;
            _appVm.HideNoAchievements = _settingsVM.HideNoAchievements;
            _appVm.HideUnstarted = _settingsVM.HideUnstarted;
            _appVm.Reverse = _settingsVM.Reverse;
            _appVm.OrderBy = _settingsVM.OrderBy;
            if (_isPhone)
            {
                _appVm.SingleColumnLayout = true;
                _settingsVM.SingleColumnLayout = true;
            }
            else
            {
                _appVm.SingleColumnLayout = _settingsVM.SingleColumnLayout;
            }

            _appVm.CardProgressStyle = _settingsVM.CardProgressStyle;

            // Update layout based on current settings
            UpdateItemsLayout();
            UpdateFillMode();

            if (!_appVm.InitialLoadCompleted)
            {
                await _appVm.ConfigureProviders();

                if (_settingsVM.Games.Count == 0)
                {
                    Log.Information("No games found, scanning.");
                    _settingsVM.Games = [];
                    ScanCommand.Execute(null);
                }
                else
                {
                    Log.Information(_settingsVM.Games.Count + " games found, refreshing.");
                    RefreshCommand.Execute(null);
                }
                _appVm.InitialLoadCompleted = true;
            }
            else
            {
                // If we've already loaded once, just populate the filtered games
                _appVm.FilteredGames.ReplaceAll(_settingsVM.Games.Values);
                ApplyFilters();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Initial load failed");
        }
    }

    /// <summary>
    /// Does a full scan of users library for each platform
    /// if we don't have any games from that provider.
    /// </summary>
    private async Task Scan()
    {
        // Cancel any existing operation and create a new token source
        await CancelCurrentOperation();
        _operationCts = new CancellationTokenSource();
        var ct = _operationCts.Token;

        _appVm.IsScanningOrRefreshing = true;

        // Snapshot current games so we can restore on failure/cancellation
        var previousGames = _settingsVM.Games;

        // Seed per-provider status items
        _appVm.ProviderScanStatuses.Clear();
        foreach (var provider in _appVm.Providers)
        {
            _appVm.ProviderScanStatuses.Add(AppVM.ProviderScanStatus.Create(provider));
        }

        try
        {
            // Run each provider sequentially so we get a clean per-provider progress bar
            var results = new List<Game[]>();
            for (int i = 0; i < _appVm.Providers.Count; i++)
            {
                var provider = _appVm.Providers[i];
                ct.ThrowIfCancellationRequested();

                var status = _appVm.ProviderScanStatuses[i];
                status.Status = "Scanning...";
                status.Progress = 0.05; // visible but not full until the first game completes

                // Progress<T> captures this (UI-thread) SynchronizationContext, so per-game
                // reports from the provider's background workers marshal back here safely.
                var reporter = new Progress<ScanProgress>(p =>
                {
                    status.Progress = p.Total > 0 ? (double)p.Current / p.Total : 0.05;
                    status.Status = $"Scanning... ({p.Current}/{p.Total})";
                });

                Log.Information("Scanning for games from {Provider}", provider.GetType().Name);
                var games = await provider.GetLibrary(reporter, ct);

                status.GameCount = games.Length;
                status.Status = games.Length > 0
                    ? $"Found {games.Length} games"
                    : "No games found";
                status.Progress = 1.0;

                Log.Information("Found {Count} games from {Provider}", games.Length, provider.GetType().Name);
                results.Add(games);
            }

            ct.ThrowIfCancellationRequested();

            // Stage all results into a fresh dictionary before committing
            _settingsVM.Games = [];

            // Add all games using batch update for single notification
            using (_settingsVM.BeginBatchUpdate())
            {
                foreach (var games in results)
                {
                    foreach (var game in games)
                    {
                        _settingsVM.AddOrUpdateGame(game);
                    }
                }
            }

            // Fetch HowLongToBeat data for all games in parallel
            _appVm.LoadingGamesSummary = "Fetching HowLongToBeat data...";

            var gamesToFetch = _settingsVM.Games.Values
                .Where(g => !g.HowLongToBeatLastFetched.HasValue ||
                            (DateTime.UtcNow - g.HowLongToBeatLastFetched.Value).TotalDays >= 7)
                .ToList();

            await FetchHltbDataAsync(gamesToFetch, ct);

            // Clear per-provider statuses after HLTB fetch completes
            _appVm.ProviderScanStatuses.Clear();

            // Save scan results
            await _settingsVM.Save();
            Log.Information("loaded {0} games, {1} achievements",
                _settingsVM.Games.Count, _settingsVM.Games.Sum(x =>
                    x.Value.Achievements.Count));

            _appVm.LoadingGamesSummary = "";

            // Refresh the filtered games collection once after all games are loaded
            RefreshFiltered();
        }
        catch (OperationCanceledException)
        {
            Log.Information("Scan operation was cancelled");
            _appVm.LoadingGamesSummary = "Scan cancelled";
            _appVm.ProviderScanStatuses.Clear();
            // Restore the previous game list so no data is lost on cancel
            _settingsVM.Games = previousGames;
            RefreshFiltered();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Scan failed");
            _appVm.LoadingGamesSummary = "Scan failed";
            _appVm.ProviderScanStatuses.Clear();
            // Restore the previous game list so no data is lost on error
            _settingsVM.Games = previousGames;
            RefreshFiltered();
        }
        finally
        {
            _appVm.IsScanningOrRefreshing = false;
            // Reclaim the LOH fragmentation left by the scan/refresh allocation burst.
            CompactLargeObjectHeap();
        }
    }

    /// <summary>
    /// Updates users games
    /// </summary>
    private async Task Refresh()
    {
        // Cancel any existing operation and create a new token source
        await CancelCurrentOperation();
        _operationCts = new CancellationTokenSource();
        var ct = _operationCts.Token;

        _appVm.IsScanningOrRefreshing = true;

        // Seed per-provider status items
        _appVm.ProviderScanStatuses.Clear();
        foreach (var provider in _appVm.Providers)
        {
            _appVm.ProviderScanStatuses.Add(AppVM.ProviderScanStatus.Create(provider));
        }

        try
        {
            // If this is the initial load and we have games in memory, load from memory instead of providers
            if (!_appVm.InitialLoadCompleted && _settingsVM.Games.Count > 0)
            {
                _appVm.LoadingGamesSummary = "Loading games from memory...";

                // Add a small delay to show the loading state
                await Task.Delay(100, ct);

                // Load all games from memory with single notification
                _appVm.FilteredGames.ReplaceAll(_settingsVM.Games.Values);

                _appVm.LoadingGamesSummary = "";
                _appVm.ProviderScanStatuses.Clear();
                ApplyFilters();
                return;
            }

            // Regular refresh logic - run each provider sequentially for per-provider progress
            var results = new List<Game[]>();
            for (int i = 0; i < _appVm.Providers.Count; i++)
            {
                var provider = _appVm.Providers[i];
                ct.ThrowIfCancellationRequested();

                var status = _appVm.ProviderScanStatuses[i];
                status.Status = "Refreshing...";
                status.Progress = 0.05; // visible but not full until the first game completes

                // Progress<T> captures this (UI-thread) SynchronizationContext, so per-game
                // reports from the provider's background workers marshal back here safely.
                var reporter = new Progress<ScanProgress>(p =>
                {
                    status.Progress = p.Total > 0 ? (double)p.Current / p.Total : 0.05;
                    status.Status = $"Refreshing... ({p.Current}/{p.Total})";
                });

                Log.Information("Getting recent games from {Provider}", provider.GetType().Name);
                var providerGames = await provider.RefreshLibrary(reporter, ct);

                status.GameCount = providerGames.Length;
                status.Status = providerGames.Length > 0
                    ? $"Updated {providerGames.Length} games"
                    : "No updates";
                status.Progress = 1.0;

                Log.Information("Found {Count} games from {Provider}", providerGames.Length, provider.GetType().Name);
                results.Add(providerGames);
            }

            ct.ThrowIfCancellationRequested();

            var games = results.SelectMany(g => g).ToList();

            // Update or add new games using batch update
            using (_settingsVM.BeginBatchUpdate())
            {
                foreach (var newGame in games)
                {
                    _settingsVM.AddOrUpdateGame(newGame);
                }
            }

            // Fetch HowLongToBeat data for new/updated games in parallel
            var gamesToFetch = games
                .Where(g => !g.HowLongToBeatLastFetched.HasValue ||
                            (DateTime.UtcNow - g.HowLongToBeatLastFetched.Value).TotalDays >= 7)
                .ToList();

            if (gamesToFetch.Count > 0)
            {
                _appVm.LoadingGamesSummary = "Fetching HowLongToBeat data...";
                Log.Information("Fetching HLTB data for {Count} refreshed games", gamesToFetch.Count);
                await FetchHltbDataAsync(gamesToFetch, ct);
            }

            // Clear per-provider statuses after HLTB fetch completes
            _appVm.ProviderScanStatuses.Clear();

            // Save and repopulate FilteredGames with all games from memory
            await _settingsVM.Save();

            // Load ALL games from memory with single notification
            _appVm.FilteredGames.ReplaceAll(_settingsVM.Games.Values);

            _appVm.LoadingGamesSummary = "";
            ApplyFilters();
        }
        catch (OperationCanceledException)
        {
            Log.Information("Refresh operation was cancelled");
            _appVm.LoadingGamesSummary = "Refresh cancelled";
            _appVm.ProviderScanStatuses.Clear();
            // Still refresh with whatever games we have
            RefreshFiltered();
        }
        finally
        {
            _appVm.IsScanningOrRefreshing = false;
            // Reclaim the LOH fragmentation left by the scan/refresh allocation burst.
            CompactLargeObjectHeap();
        }
    }

    /// <summary>
    /// Fetches HowLongToBeat data for each game in <paramref name="gamesToFetch"/>,
    /// limiting concurrency to 5 at a time. Resolves the HLTB service from DI
    /// (it is a singleton — do NOT dispose it here).
    /// Progress is reported via <see cref="AppVM.LoadingGamesSummary"/>.
    /// </summary>
    private async Task FetchHltbDataAsync(IReadOnlyCollection<Game> gamesToFetch, CancellationToken ct)
    {
        if (gamesToFetch.Count == 0)
            return;

        Log.Information("Fetching HLTB data for {Count} games", gamesToFetch.Count);

        // Resolve the singleton from DI — do NOT dispose
        var hltbService = Ioc.Default.GetRequiredService<HowLongToBeatService>();

        // Limit concurrent requests to 5
        using var semaphore = new SemaphoreSlim(5, 5);
        var completedCount = 0;
        var totalCount = gamesToFetch.Count;

        var tasks = gamesToFetch.Select(async game =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                // FetchAndUpdateGameData mutates the Game in place (it is already in the
                // Games dictionary). We do NOT call AddOrUpdateGame per task here: doing so
                // under BeginBatchUpdate from these concurrent background tasks both races the
                // batch-depth counter and fires the Games notification off the UI thread.
                await hltbService.FetchAndUpdateGameData(game, ct);

                var current = Interlocked.Increment(ref completedCount);
                _appVm.LoadingGamesSummary = $"Fetching HowLongToBeat data ({current}/{totalCount})";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fetch HLTB data for {GameName}", game.Name);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // Persist + fire a single Games notification once, back on the UI thread (this
        // continuation resumes on the captured context), rather than per-game off-thread.
        _settingsVM.AddOrUpdateGames(gamesToFetch);
    }

    /// <summary>
    /// Cancels the current scan/refresh operation
    /// </summary>
    private async Task CancelCurrentOperation()
    {
        if (_operationCts != null)
        {
            await _operationCts.CancelAsync();
            _operationCts.Dispose();
            _operationCts = null;
        }
    }

    // Public method to apply filters from MainPage
    public void ApplyFilters()
    {
        RefreshFiltered();
    }

    private int _filterVersion;

    private async void RefreshFiltered()
    {
        // Snapshot the games dictionary values on the UI thread before going background.
        // This avoids iterating _settingsVM.Games while it may be mutated on another thread.
        var snapshot = _settingsVM.Games.Values.ToList();

        // Capture all filter/sort parameters as immutable value (safe for background thread)
        var args = new FilterArgs(
            HideComplete:       _appVm.HideComplete,
            HideNoAchievements: _appVm.HideNoAchievements,
            HideUnstarted:      _appVm.HideUnstarted,
            Reverse:            _appVm.Reverse,
            SearchText:         _appVm.SearchText,
            OrderBy:            _appVm.OrderBy,
            SteamFilter:        LibraryVM.SteamFilter,
            RAFilter:           LibraryVM.RAFilter,
            PSNFilter:          LibraryVM.PSNFilter,
            XBFilter:           LibraryVM.XBFilter,
            // Merge editions is a persisted appearance setting (toggled on the Settings page), so read
            // it straight from SettingsVM — same as MergedCompletionMode below. The debug "show only
            // merged" toggle still lives on LibraryVM.
            MergeEditions:      _settingsVM.MergeEditions,
            ShowOnlyMerged:     LibraryVM.ShowOnlyMerged,
            MergedCompletionMode: _settingsVM.MergedCompletionMode);

        var version = ++_filterVersion;

        var filtered = await Task.Run(() => LibraryVM.FilterAndSort(snapshot, args));

        // Discard stale results if a newer filter was triggered
        if (_filterVersion != version)
            return;

        // Back on UI thread — give ItemsRepeater the full list, it virtualizes natively
        _appVm.FilteredGames.ReplaceAll(filtered);

        // Drive the empty-state overlays: distinguish "no games at all" from "filtered to zero".
        _appVm.LibraryIsEmpty = snapshot.Count == 0;
        _appVm.HasNoVisibleGames = snapshot.Count > 0 && filtered.Count == 0;

        // Ensure layout settings are maintained after filtering
        UpdateItemsLayout();
    }

    /// <summary>
    /// Clears all filters, search, and platform toggles back to defaults (everything shown),
    /// persists the change, and re-applies. Wired to the "Reset filters" button on the
    /// filtered-to-zero empty state.
    /// </summary>
    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        _appVm.HideComplete = false;
        _appVm.HideNoAchievements = false;
        _appVm.HideUnstarted = false;
        _appVm.Reverse = false;
        _appVm.OrderBy = OrderBy.Name;
        _appVm.SearchText = string.Empty;

        LibraryVM.SteamFilter = true;
        LibraryVM.RAFilter = true;
        LibraryVM.PSNFilter = true;
        LibraryVM.XBFilter = true;

        _settingsVM.HideComplete = false;
        _settingsVM.HideNoAchievements = false;
        _settingsVM.HideUnstarted = false;
        _settingsVM.Reverse = false;
        _settingsVM.OrderBy = OrderBy.Name;
        _settingsVM.SteamFilter = true;
        _settingsVM.RAFilter = true;
        _settingsVM.PSNFilter = true;
        _settingsVM.XBFilter = true;
        await _settingsVM.Save();

        ApplyFilters();
    }

    // Windowing removed — ItemsRepeater handles virtualization natively

    /// <summary>
    /// Open Game Page
    /// </summary>
    private void OpenGame(object sender, TappedRoutedEventArgs e)
    {
        Game game = (Game)((FrameworkElement)sender).DataContext;
        _appVm.SelectedGame = game;
        App.Frame?.Navigate(typeof(GamePage));
    }

    // Public method to update layout from MainPage
    public new void UpdateLayout()
    {
        UpdateItemsLayout();
    }

    // Method to update layout based on toggle state - swaps ItemsRepeater layout
    private void UpdateItemsLayout()
    {
        if (_isPhone)
        {
            _appVm.SingleColumnLayout = true;
            gameRepeater.Layout = _listLayout;
            return;
        }

        gameRepeater.Layout = _appVm.SingleColumnLayout ? _listLayout : _gridLayout;
    }

    // Public method to swap the game card template when the progress style changes. Only the
    // filled-background style needs the dedicated template; Bar and None both use GameTemplate
    // (whose progress bar binds its visibility to the style, so None hides it).
    public void UpdateFillMode()
    {
        var key = _appVm.CardProgressStyle == CardProgressStyle.FilledBackground
            ? "GameFillTemplate"
            : "GameTemplate";
        if (Resources[key] is DataTemplate template)
        {
            gameRepeater.ItemTemplate = template;
        }
    }

    private void OnScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        var delta = e.GetCurrentPoint(sv).Properties.MouseWheelDelta;
        sv.ChangeView(null, sv.VerticalOffset - delta, null, true);
        e.Handled = true;
    }

    /// <summary>
    /// Compacts the Large Object Heap once, then collects. A scan/refresh allocates many large,
    /// short-lived buffers (provider JSON, the ~20 MB settings blob, per-image decode streams);
    /// the LOH is never compacted by an ordinary GC, so that burst leaves ~140 MB of trapped free
    /// space (measured after a full refresh). Doing it explicitly once the burst is over returns
    /// those pages to the OS. Cheap and infrequent — scans are user-initiated and the loading UI
    /// is already shown — so the one blocking collection here is not in any hot path.
    /// </summary>
    private static void CompactLargeObjectHeap()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
    }

    #region Async Commands
    private AsyncCommand? _refreshCommand;
    public AsyncCommand RefreshCommand => _refreshCommand ??= new AsyncCommand(Refresh);
    private AsyncCommand? _scanCommand;
    public AsyncCommand ScanCommand => _scanCommand ??= new AsyncCommand(Scan);
    private AsyncCommand? _cancelCommand;
    public AsyncCommand CancelCommand => _cancelCommand ??= new AsyncCommand(CancelCurrentOperation);
    #endregion
}
