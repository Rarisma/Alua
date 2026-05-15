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

    // Commands for layouts
    public AsyncCommand SingleColumnCommand => new(async () => {
        _appVm.SingleColumnLayout = true;
        _settingsVM.SingleColumnLayout = true;
        UpdateItemsLayout();
        await _settingsVM.Save();
    });

    public AsyncCommand MultiColumnCommand => new(async () => {
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

    public Library() { InitializeComponent(); }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Override scroll handling for faster trackpad scrolling on desktop
            if (!_isPhone)
                gamesScrollViewer.AddHandler(PointerWheelChangedEvent,
                    new PointerEventHandler(OnScrollViewerPointerWheelChanged), true);

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

            _appVm.FillBackgroundProgress = _settingsVM.FillBackgroundProgress;

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

        try
        {
            _settingsVM.Games = [];

            // Run all providers in parallel for faster scanning
            var providerTasks = _appVm.Providers.Select(async provider =>
            {
                ct.ThrowIfCancellationRequested();
                Log.Information("Scanning for games from {Provider}", provider.GetType().Name);
                var games = await provider.GetLibrary(ct);
                Log.Information("Found {Count} games from {Provider}", games.Length, provider.GetType().Name);
                return games;
            });

            var results = await Task.WhenAll(providerTasks);
            ct.ThrowIfCancellationRequested();

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

            Log.Information("Fetching HLTB data for {Count} games", gamesToFetch.Count);

            if (gamesToFetch.Any())
            {
                // Use a semaphore to limit concurrent requests to 5
                using var semaphore = new SemaphoreSlim(5, 5);
                // Share a single HowLongToBeatService instance across all tasks
                using var hltbService = new HowLongToBeatService();
                var completedCount = 0;
                var totalCount = gamesToFetch.Count;

                var tasks = gamesToFetch.Select(async game =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        await hltbService.FetchAndUpdateGameData(game);

                        var current = Interlocked.Increment(ref completedCount);
                        _appVm.LoadingGamesSummary = $"Fetching HowLongToBeat data ({current}/{totalCount})";

                        _settingsVM.AddOrUpdateGame(game);
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
            }

            //Save scan results
            await _settingsVM.Save();
            Log.Information("loaded {0} games, {1} achievements",
                _settingsVM.Games.Count, _settingsVM.Games.Sum(x =>
                    x.Value.Achievements.Count));

            // Show a message or update a property for the UI
            _appVm.LoadingGamesSummary = "";

            // Refresh the filtered games collection once after all games are loaded
            RefreshFiltered();
        }
        catch (OperationCanceledException)
        {
            Log.Information("Scan operation was cancelled");
            _appVm.LoadingGamesSummary = "Scan cancelled";
            // Still refresh with whatever games we have
            RefreshFiltered();
        }
        finally
        {
            _appVm.IsScanningOrRefreshing = false;
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

        try
        {
            _appVm.LoadingGamesSummary = "Preparing to refresh games...";

            // If this is the initial load and we have games in memory, load from memory instead of providers
            if (!_appVm.InitialLoadCompleted && _settingsVM.Games.Count > 0)
            {
                _appVm.LoadingGamesSummary = "Loading games from memory...";

                // Add a small delay to show the loading state
                await Task.Delay(100, ct);

                // Load all games from memory with single notification
                _appVm.FilteredGames.ReplaceAll(_settingsVM.Games.Values);

                _appVm.LoadingGamesSummary = "";
                ApplyFilters();
                return;
            }

            // Regular refresh logic - get recent games from providers IN PARALLEL
            var providerTasks = _appVm.Providers.Select(async provider =>
            {
                ct.ThrowIfCancellationRequested();
                Log.Information("Getting recent games from {Provider}", provider.GetType().Name);
                var providerGames = await provider.RefreshLibrary(ct);
                Log.Information("Found {Count} games from {Provider}", providerGames.Length, provider.GetType().Name);
                return providerGames;
            });

            var results = await Task.WhenAll(providerTasks);
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
            var gamesToFetch = games.Where(g => !g.HowLongToBeatLastFetched.HasValue ||
                                               (DateTime.UtcNow - g.HowLongToBeatLastFetched.Value).TotalDays >= 7)
                                    .ToList();

            if (gamesToFetch.Any())
            {
                _appVm.LoadingGamesSummary = "Fetching HowLongToBeat data...";
                Log.Information("Fetching HLTB data for {Count} refreshed games", gamesToFetch.Count);

                // Use a semaphore to limit concurrent requests to 5
                using var semaphore = new SemaphoreSlim(5, 5);
                // Share a single HowLongToBeatService instance across all tasks
                using var hltbService = new HowLongToBeatService();
                var completedCount = 0;
                var totalCount = gamesToFetch.Count;

                var tasks = gamesToFetch.Select(async game =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        await hltbService.FetchAndUpdateGameData(game);

                        var current = Interlocked.Increment(ref completedCount);
                        _appVm.LoadingGamesSummary = $"Fetching HowLongToBeat data ({current}/{totalCount})";

                        _settingsVM.AddOrUpdateGame(game);
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
            }

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
            // Still refresh with whatever games we have
            RefreshFiltered();
        }
        finally
        {
            _appVm.IsScanningOrRefreshing = false;
        }
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
        // Capture filter/sort parameters as locals (safe for background thread access)
        var games = _settingsVM.Games ?? new();
        var hideComplete = _appVm.HideComplete;
        var hideNoAchievements = _appVm.HideNoAchievements;
        var hideUnstarted = _appVm.HideUnstarted;
        var searchText = _appVm.SearchText;
        var orderBy = _appVm.OrderBy;

        var version = ++_filterVersion;

        var filtered = await Task.Run(() =>
        {
            var list = games
                .Where(g => !hideComplete || g.Value.UnlockedCount < g.Value.Achievements.Count)
                .Where(g => !hideNoAchievements || g.Value.HasAchievements)
                .Where(g => !hideUnstarted || g.Value.UnlockedCount > 0);

            // Apply text search filter
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                list = list.Where(g => g.Value.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase));
            }

            // Filter out games without HLTB data when sorting by HLTB
            if (orderBy == HowLongToBeatMain)
            {
                list = list.Where(g => g.Value.HowLongToBeatMain.HasValue);
            }
            else if (orderBy == HowLongToBeatCompletionist)
            {
                list = list.Where(g => g.Value.HowLongToBeatCompletionist.HasValue);
            }

            list = orderBy switch
            {
                OrderBy.Name => list.OrderBy(g => g.Value.Name),
                NameReverse => list.OrderByDescending(g => g.Value.Name),
                CompletionPct => list.OrderByDescending(g => (double)g.Value.UnlockedCount / g.Value.Achievements.Count),
                TotalCount => list.OrderBy(g => g.Value.Achievements.Count),
                UnlockedCount => list.OrderBy(g => g.Value.UnlockedCount),
                Playtime => list.OrderBy(g => g.Value.PlaytimeMinutes),
                LastPlayed => list.OrderByDescending(g => g.Value.LastPlayed ?? DateTime.MinValue),
                HowLongToBeatMain => list.OrderBy(g => g.Value.HowLongToBeatMain.Value),
                HowLongToBeatCompletionist => list.OrderBy(g => g.Value.HowLongToBeatCompletionist.Value),
                _ => list
            };
            
            
            //per platform filtering
            Dictionary<Platforms, bool> Enabled = new()
            {
                { Platforms.Steam, LibraryVM.SteamFilter },
                { Platforms.RetroAchievements, LibraryVM.RAFilter },
                { Platforms.PlayStation, LibraryVM.PSNFilter },
                { Platforms.Xbox, LibraryVM.XBFilter },
            };
            
            list = list.Select(g => g)
                .Where(g => Enabled[g.Value.Platform]);
                
            return list.Select(g => g.Value).ToList();
        });

        // Discard stale results if a newer filter was triggered
        if (_filterVersion != version)
            return;

        // Back on UI thread — give ItemsRepeater the full list, it virtualizes natively
        _allFilteredGames = filtered;
        _appVm.FilteredGames.ReplaceAll(filtered);

        // Ensure layout settings are maintained after filtering
        UpdateItemsLayout();
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

    // Public method to swap the game card template when fill-background toggle changes
    public void UpdateFillMode()
    {
        var key = _appVm.FillBackgroundProgress ? "GameFillTemplate" : "GameTemplate";
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

    #region Async Commands
    private AsyncCommand? _refreshCommand;
    public AsyncCommand RefreshCommand => _refreshCommand ??= new AsyncCommand(Refresh);
    private AsyncCommand? _scanCommand;
    public AsyncCommand ScanCommand => _scanCommand ??= new AsyncCommand(Scan);
    private AsyncCommand? _cancelCommand;
    private List<Game> _allFilteredGames;
    public AsyncCommand CancelCommand => _cancelCommand ??= new AsyncCommand(CancelCurrentOperation);
    #endregion
}
