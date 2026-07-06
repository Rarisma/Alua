using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

//FHN walked so Alua could run.
namespace Alua.UI;

public sealed partial class MainPage : Page
{
    LibraryVM _libraryVM = Ioc.Default.GetRequiredService<LibraryVM>();
    AppVM _appVM = Ioc.Default.GetRequiredService<AppVM>();
    SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    private Library? _currentGameList;
    private readonly bool _isPhone = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    private CancellationTokenSource? _searchDebounce;

    /// <summary>
    /// Visible only in Debug builds — gates the diagnostic "Show only merged" filter so it never
    /// ships to end users. XAML can't use #if directly, so the flyout binds this for visibility.
    /// </summary>
    public Visibility DebugOnlyVisibility =>
#if DEBUG
        Visibility.Visible;
#else
        Visibility.Collapsed;
#endif

    public MainPage()
    {
        InitializeComponent();
        // Phones are always single-column; appearance/layout prefs now live on the Settings page.
        if (_isPhone)
        {
            _appVM.SingleColumnLayout = true;
            _settingsVM.SingleColumnLayout = true;
        }
        App.Frame = AppContentFrame;
        App.Frame.Navigated += OnFrameNavigated;

        // Unsubscribe when page is unloaded to prevent memory leaks
        Unloaded += OnUnloaded;

        if (_settingsVM.Initialised)
        {
            // Restore persisted filter/sort state into the live VMs BEFORE navigating, so the
            // Library page's RestoreFilterUIFromVM (raised on Navigated, ahead of Library.OnLoaded)
            // reflects the saved values. Do NOT call Filter_Changed here: it reads state FROM the
            // still-empty UI controls back into the VM, which clobbered everything we just loaded
            // and reset every filter on launch.
            _appVM.HideComplete = _settingsVM.HideComplete;
            _appVM.HideNoAchievements = _settingsVM.HideNoAchievements;
            _appVM.HideUnstarted = _settingsVM.HideUnstarted;
            _appVM.Reverse = _settingsVM.Reverse;
            _appVM.OrderBy = _settingsVM.OrderBy;

            // Platform toggles live on LibraryVM (TwoWay-bound to the flyout buttons).
            _libraryVM.SteamFilter = _settingsVM.SteamFilter;
            _libraryVM.RAFilter = _settingsVM.RAFilter;
            _libraryVM.PSNFilter = _settingsVM.PSNFilter;
            _libraryVM.XBFilter = _settingsVM.XBFilter;
            // EditionDisplayMode is read directly from SettingsVM in Library.ApplyFilters (it's a
            // Settings-page control now), so it doesn't need mirroring onto LibraryVM here.

            App.Frame.Navigate(typeof(Library));
        }
        else
        {
            //Hide nav bar.
            App.Frame.Navigate(typeof(Initialize));
            _appVM.CommandBarVisibility = Visibility.Collapsed;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe from events to prevent memory leaks
        if (App.Frame != null)
        {
            App.Frame.Navigated -= OnFrameNavigated;
        }
        Unloaded -= OnUnloaded;
    }

    private async void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        try
        {
            await _settingsVM.Save();

            // Show/hide game list controls based on current page
            if (e.Content is Library gameList)
            {
                _currentGameList = gameList;
                _appVM.GameListControlsVisibility = Visibility.Visible;
                RestoreFilterUIFromVM();
            }
            else
            {
                _currentGameList = null;
                _appVM.GameListControlsVisibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error handling frame navigation");
        }
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        _appVM.InitialLoadCompleted = false; //Will force reload al providers.
        App.Frame.Navigate(typeof(Settings));
    }

    private void OpenGamesList(object sender, RoutedEventArgs e) => App.Frame.Navigate(typeof(Library));
    private void Back(object sender, RoutedEventArgs e) => App.Frame.GoBack();
    
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _currentGameList?.RefreshCommand?.Execute(null);
    }
    
    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = (TextBox)sender;
        _appVM.SearchText = textBox.Text;

        // Debounce search to avoid filtering on every keystroke
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();

        try
        {
            await Task.Delay(250, _searchDebounce.Token);
            _currentGameList?.ApplyFilters();
        }
        catch (TaskCanceledException)
        {
            // Expected when user types next character before delay completes
        }
    }
    
    private async void Filter_Changed(object? sender, RoutedEventArgs? e)
    {
        try
        {
            // Update VM properties from UI controls
            _appVM.HideComplete = CheckHideComplete.IsChecked == true;
            _appVM.HideNoAchievements = CheckNoAchievements.IsChecked == true;
            _appVM.HideUnstarted = CheckUnstarted.IsChecked == true;

            // Read which item is selected in the ComboBox
            if (SortByComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string tag)
            {
                _appVM.OrderBy = Enum.Parse<OrderBy>(tag);
            }

            // Persist settings
            _settingsVM.HideComplete = _appVM.HideComplete;
            _settingsVM.HideNoAchievements = _appVM.HideNoAchievements;
            _settingsVM.HideUnstarted = _appVM.HideUnstarted;
            _settingsVM.Reverse = _appVM.Reverse;
            _settingsVM.OrderBy = _appVM.OrderBy;

            // Platform toggles are TwoWay-bound to LibraryVM; mirror them so they persist too.
            _settingsVM.SteamFilter = _libraryVM.SteamFilter;
            _settingsVM.RAFilter = _libraryVM.RAFilter;
            _settingsVM.PSNFilter = _libraryVM.PSNFilter;
            _settingsVM.XBFilter = _libraryVM.XBFilter;
            // EditionDisplayMode is owned by the Settings page (persisted on SettingsVM); don't mirror
            // a stale LibraryVM value back over it here, or the Settings control gets clobbered.

            await _settingsVM.Save();

            // Update filter icon based on whether any filters are active
            UpdateFilterIcon();

            // Apply filters if GameList is current page
            _currentGameList?.ApplyFilters();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error applying filters");
        }
    }
    
    // The filter controls live inside this flyout, whose content Uno only loads into the live
    // visual tree when it is first shown. Imperative values set on them at navigation time (before
    // the content is loaded) get reset to their XAML defaults on first open — which is why the
    // checkboxes/sort selection never reflected the persisted state. Re-sync from the VM here, once
    // the content is realized, so the saved filters actually appear. (The platform toggle buttons
    // are x:Bind-bound and re-evaluate on show, so they don't need this.)
    private void OnFilterFlyoutOpened(object? sender, object e) => RestoreFilterUIFromVM();

    private void RestoreFilterUIFromVM()
    {
        // Restore checkbox states from VM
        CheckHideComplete.IsChecked = _appVM.HideComplete;
        CheckNoAchievements.IsChecked = _appVM.HideNoAchievements;
        CheckUnstarted.IsChecked = _appVM.HideUnstarted;
        
        // Restore ComboBox selection from VM
        var orderByString = _appVM.OrderBy.ToString();
        foreach (ComboBoxItem item in SortByComboBox.Items)
        {
            if (item.Tag?.ToString() == orderByString)
            {
                SortByComboBox.SelectedItem = item;
                break;
            }
        }

        // Search box
        SearchBox.Text = _appVM.SearchText ?? string.Empty;
        
        // Update filter icon
        UpdateFilterIcon();
    }
    
    private void UpdateFilterIcon()
    {
        // Check if any filters are active
        bool filtersActive = _appVM.HideComplete ||
                            _appVM.HideNoAchievements ||
                            _appVM.HideUnstarted ||
                            _appVM.OrderBy != OrderBy.Name ||
                            _appVM.Reverse;

        // Update the filter icon glyph - E71C is empty filter, E16E is filled filter
        FilterIcon.Glyph = filtersActive ? "\uE16E" : "\uE71C";
    }

    private void ErrorInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _appVM.ClearError();
    }
}
