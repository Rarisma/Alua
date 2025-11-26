using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using static Alua.Services.ViewModels.OrderBy;
//FHN walked so Alua could run.
namespace Alua.UI;

public sealed partial class MainPage : Page
{
    AppVM _appVM = Ioc.Default.GetRequiredService<AppVM>();
    SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    private Library? _currentGameList;
    private readonly bool _isPhone = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    private CancellationTokenSource? _searchDebounce;

    public MainPage()
    {
        InitializeComponent();
        if (_isPhone)
        {
            LayoutOptionsPanel.Visibility = Visibility.Collapsed;
            LayoutToggle.IsOn = true;
            LayoutToggle.IsEnabled = false;
            _appVM.SingleColumnLayout = true;
            _settingsVM.SingleColumnLayout = true;
        }
        else
        {
            LayoutOptionsPanel.Visibility = Visibility.Visible;
            LayoutToggle.IsEnabled = true;
        }
        App.Frame = AppContentFrame;
        App.Frame.Navigated += OnFrameNavigated;

        // Unsubscribe when page is unloaded to prevent memory leaks
        Unloaded += OnUnloaded;

        if (_settingsVM.Initialised)
        {
            App.Frame.Navigate(typeof(Library));
        }
        else
        {
            //Hide nav bar.
            App.Frame.Navigate(typeof(Initalize));
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
            _appVM.Reverse = CheckReverse.IsChecked == true;

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
    
    private async void ToggleLayout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var toggle = (ToggleSwitch)sender;
            if (_isPhone)
            {
                if (!toggle.IsOn)
                {
                    toggle.IsOn = true;
                }
                return;
            }
            _appVM.SingleColumnLayout = toggle.IsOn;
            _settingsVM.SingleColumnLayout = toggle.IsOn;
            _currentGameList?.UpdateLayout();
            await _settingsVM.Save();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error toggling layout");
        }
    }
    
    private void RestoreFilterUIFromVM()
    {
        // Restore checkbox states from VM
        CheckHideComplete.IsChecked = _appVM.HideComplete;
        CheckNoAchievements.IsChecked = _appVM.HideNoAchievements;
        CheckUnstarted.IsChecked = _appVM.HideUnstarted;
        CheckReverse.IsChecked = _appVM.Reverse;

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

        // Layout toggle
        if (_isPhone)
        {
            LayoutOptionsPanel.Visibility = Visibility.Collapsed;
            LayoutToggle.IsEnabled = false;
            LayoutToggle.IsOn = true;
        }
        else
        {
            LayoutOptionsPanel.Visibility = Visibility.Visible;
            LayoutToggle.IsEnabled = true;
            LayoutToggle.IsOn = _appVM.SingleColumnLayout;
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
