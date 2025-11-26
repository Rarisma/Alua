using System.ComponentModel;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Alua.UI.Controls;

public sealed partial class ProfileCard : UserControl
{
    private readonly AggregateStatisticsService _statsService = Ioc.Default.GetRequiredService<AggregateStatisticsService>();

    // Bind to cached values from the statistics service
    private int TotalGames => _statsService.TotalGames;
    private int Unlocked => _statsService.UnlockedCount;
    private int Perfect => _statsService.PerfectGames;
    private int PercentComplete => _statsService.PercentComplete;

    public ProfileCard()
    {
        InitializeComponent();
        _statsService.PropertyChanged += StatsServiceOnPropertyChanged;
    }

    private void StatsServiceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Bindings.Update();
    }
}
