using Alua.Services;
using Alua.Services.ViewModels;
using Alua.UI.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using Serilog;
using Microsoft.UI.Xaml.Controls;
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

    // Windowed virtualization state
    private int _windowStartIndex;  // Index into _allFilteredGames of first displayed item
    private int _windowEndIndex;    // Index into _allFilteredGames of last displayed item (exclusive)
    private List<Game> _allFilteredGames = new();
    private bool _isLoadingMore;
    private double _lastScrollOffset;
    private const int WindowBuffer = 100;  // Items to keep above/below visible area

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

            // Update layout based on current settings
            UpdateItemsLayout();
            
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

            // Save and repopulate FilteredGames with ALL games from memory
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

    private void RefreshFiltered()
    {
        var list = (_settingsVM.Games ?? new ())
            .Where(g => !_appVm.HideComplete || g.Value.UnlockedCount < g.Value.Achievements.Count)
            .Where(g => !_appVm.HideNoAchievements || g.Value.HasAchievements)
            .Where(g => !_appVm.HideUnstarted || g.Value.UnlockedCount > 0);

        // Apply text search filter
        if (!string.IsNullOrWhiteSpace(_appVm.SearchText))
        {
            list = list.Where(g => g.Value.Name.Contains(_appVm.SearchText, StringComparison.OrdinalIgnoreCase));
        }

        // Filter out games without HLTB data when sorting by HLTB
        if (_appVm.OrderBy == HowLongToBeatMain)
        {
            list = list.Where(g => g.Value.HowLongToBeatMain.HasValue);
        }
        else if (_appVm.OrderBy == HowLongToBeatCompletionist)
        {
            list = list.Where(g => g.Value.HowLongToBeatCompletionist.HasValue);
        }

        list = _appVm.OrderBy switch
        {
            OrderBy.Name => list.OrderBy(g => g.Value.Name),
            CompletionPct => list.OrderBy(g => (double)g.Value.UnlockedCount / g.Value.Achievements.Count),
            TotalCount => list.OrderBy(g => g.Value.Achievements.Count),
            UnlockedCount => list.OrderBy(g => g.Value.UnlockedCount),
            Playtime => list.OrderBy(g => g.Value.PlaytimeMinutes),
            LastUpdated => list.OrderByDescending(g => g.Value.LastUpdated),
            HowLongToBeatMain => list.OrderBy(g => g.Value.HowLongToBeatMain.Value),
            HowLongToBeatCompletionist => list.OrderBy(g => g.Value.HowLongToBeatCompletionist.Value),
            _ => list
        };

        if (_appVm.Reverse) { list = list.Reverse(); }

        // Store all filtered games for windowed virtualization
        _allFilteredGames = list.Select(g => g.Value).ToList();
        _windowStartIndex = 0;
        _windowEndIndex = 0;
        _lastScrollOffset = 0;

        // Load initial window
        LoadInitialWindow();

        // Ensure layout settings are maintained after filtering
        UpdateItemsLayout();
    }

    /// <summary>
    /// Loads the initial window of items
    /// </summary>
    private void LoadInitialWindow()
    {
        var pageSize = _settingsVM.PageSize > 0 ? _settingsVM.PageSize : 100;
        var initialCount = Math.Min(pageSize, _allFilteredGames.Count);

        _windowStartIndex = 0;
        _windowEndIndex = initialCount;

        var initialItems = _allFilteredGames.Take(initialCount).ToList();
        _appVm.FilteredGames.ReplaceAll(initialItems);

        Log.Debug("Loaded initial window: 0-{End} of {Total}", _windowEndIndex, _allFilteredGames.Count);
    }

    /// <summary>
    /// Extends the window forward (when scrolling down)
    /// </summary>
    private void ExtendWindowForward()
    {
        if (_isLoadingMore || _windowEndIndex >= _allFilteredGames.Count)
            return;

        _isLoadingMore = true;

        try
        {
            var pageSize = _settingsVM.PageSize > 0 ? _settingsVM.PageSize : 100;
            var itemsToAdd = _allFilteredGames
                .Skip(_windowEndIndex)
                .Take(pageSize)
                .ToList();

            foreach (var item in itemsToAdd)
            {
                _appVm.FilteredGames.Add(item);
            }

            _windowEndIndex += itemsToAdd.Count;
            Log.Debug("Extended window forward: {Start}-{End} of {Total}",
                _windowStartIndex, _windowEndIndex, _allFilteredGames.Count);
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    /// <summary>
    /// Trims items from the beginning of the window (when scrolling down)
    /// </summary>
    private void TrimWindowStart(int itemsToRemove)
    {
        if (_isLoadingMore || itemsToRemove <= 0 || _windowStartIndex + itemsToRemove >= _windowEndIndex)
            return;

        _isLoadingMore = true;

        try
        {
            // Remove items from the beginning
            for (int i = 0; i < itemsToRemove && _appVm.FilteredGames.Count > 0; i++)
            {
                _appVm.FilteredGames.RemoveAt(0);
            }

            _windowStartIndex += itemsToRemove;
            Log.Debug("Trimmed window start: {Start}-{End} of {Total}",
                _windowStartIndex, _windowEndIndex, _allFilteredGames.Count);
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    /// <summary>
    /// Extends the window backward (when scrolling up)
    /// </summary>
    private void ExtendWindowBackward()
    {
        if (_isLoadingMore || _windowStartIndex <= 0)
            return;

        _isLoadingMore = true;

        try
        {
            var pageSize = _settingsVM.PageSize > 0 ? _settingsVM.PageSize : 100;
            var itemsToLoad = Math.Min(pageSize, _windowStartIndex);
            var newStartIndex = _windowStartIndex - itemsToLoad;

            var itemsToAdd = _allFilteredGames
                .Skip(newStartIndex)
                .Take(itemsToLoad)
                .ToList();

            // Insert items at the beginning
            for (int i = itemsToAdd.Count - 1; i >= 0; i--)
            {
                _appVm.FilteredGames.Insert(0, itemsToAdd[i]);
            }

            _windowStartIndex = newStartIndex;
            Log.Debug("Extended window backward: {Start}-{End} of {Total}",
                _windowStartIndex, _windowEndIndex, _allFilteredGames.Count);
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    /// <summary>
    /// Trims items from the end of the window (when scrolling up)
    /// </summary>
    private void TrimWindowEnd(int itemsToRemove)
    {
        if (_isLoadingMore || itemsToRemove <= 0 || _windowEndIndex - itemsToRemove <= _windowStartIndex)
            return;

        _isLoadingMore = true;

        try
        {
            // Remove items from the end
            for (int i = 0; i < itemsToRemove && _appVm.FilteredGames.Count > 0; i++)
            {
                _appVm.FilteredGames.RemoveAt(_appVm.FilteredGames.Count - 1);
            }

            _windowEndIndex -= itemsToRemove;
            Log.Debug("Trimmed window end: {Start}-{End} of {Total}",
                _windowStartIndex, _windowEndIndex, _allFilteredGames.Count);
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    /// <summary>
    /// Handles scroll events for windowed virtualization
    /// </summary>
    private void OnScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_isLoadingMore || sender is not ScrollViewer scrollViewer)
            return;

        var scrollableHeight = scrollViewer.ScrollableHeight;
        var verticalOffset = scrollViewer.VerticalOffset;
        var viewportHeight = scrollViewer.ViewportHeight;

        if (scrollableHeight <= 0)
            return;

        var scrollingDown = verticalOffset > _lastScrollOffset;
        _lastScrollOffset = verticalOffset;

        var pageSize = _settingsVM.PageSize > 0 ? _settingsVM.PageSize : 100;

        if (scrollingDown)
        {
            // Scrolling down - check if we need to load more at the bottom
            var scrollPercentage = verticalOffset / scrollableHeight;
            if (scrollPercentage >= 0.8 && _windowEndIndex < _allFilteredGames.Count)
            {
                ExtendWindowForward();

                // After loading more at the bottom, check if we should unload from the top
                // Keep at least WindowBuffer items above the current scroll position
                var currentWindowSize = _windowEndIndex - _windowStartIndex;
                var maxWindowSize = pageSize * 3;  // Keep up to 3 pages in memory

                if (currentWindowSize > maxWindowSize)
                {
                    // Estimate how many items are above the viewport
                    var estimatedItemHeight = scrollableHeight / Math.Max(1, _appVm.FilteredGames.Count);
                    var itemsAboveViewport = estimatedItemHeight > 0
                        ? (int)(verticalOffset / estimatedItemHeight)
                        : 0;

                    // Only trim if we have enough items above the visible area
                    var safeToTrim = Math.Max(0, itemsAboveViewport - WindowBuffer);
                    if (safeToTrim > 0)
                    {
                        TrimWindowStart(Math.Min(safeToTrim, pageSize));

                        // Adjust scroll position to compensate for removed items
                        var removedHeight = safeToTrim * estimatedItemHeight;
                        scrollViewer.ChangeView(null, Math.Max(0, verticalOffset - removedHeight), null, true);
                    }
                }
            }
        }
        else
        {
            // Scrolling up - check if we need to load more at the top
            var scrollFromTop = verticalOffset / viewportHeight;
            if (scrollFromTop <= 0.2 && _windowStartIndex > 0)
            {
                ExtendWindowBackward();

                // After loading at the top, check if we should unload from the bottom
                var currentWindowSize = _windowEndIndex - _windowStartIndex;
                var maxWindowSize = pageSize * 3;

                if (currentWindowSize > maxWindowSize)
                {
                    // Estimate how many items are below the viewport
                    var estimatedItemHeight = scrollableHeight / Math.Max(1, _appVm.FilteredGames.Count);
                    var itemsBelowViewport = estimatedItemHeight > 0
                        ? (int)((scrollableHeight - verticalOffset - viewportHeight) / estimatedItemHeight)
                        : 0;

                    // Only trim if we have enough items below the visible area
                    var safeToTrim = Math.Max(0, itemsBelowViewport - WindowBuffer);
                    if (safeToTrim > 0)
                    {
                        TrimWindowEnd(Math.Min(safeToTrim, pageSize));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Open Game Page
    /// </summary>
    private void OpenGame(object sender, RoutedEventArgs e)
    {
        Game game = (Game)((Button)sender).DataContext;
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


    #region Async Commands
    private AsyncCommand? _refreshCommand;
    public AsyncCommand RefreshCommand => _refreshCommand ??= new AsyncCommand(Refresh);
    private AsyncCommand? _scanCommand;
    public AsyncCommand ScanCommand => _scanCommand ??= new AsyncCommand(Scan);
    private AsyncCommand? _cancelCommand;
    public AsyncCommand CancelCommand => _cancelCommand ??= new AsyncCommand(CancelCurrentOperation);
    #endregion
}
