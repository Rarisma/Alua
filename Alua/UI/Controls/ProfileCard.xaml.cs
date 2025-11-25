using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Alua.Services;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using SettingsVM = Alua.Services.ViewModels.SettingsVM;

namespace Alua.UI.Controls;

public sealed partial class ProfileCard : UserControl
{
    SettingsVM _settingsVM = Ioc.Default.GetRequiredService<SettingsVM>();
    private int Unlocked => _settingsVM.Games.Sum(g => g.Value.UnlockedCount);
    private int Perfect => _settingsVM.Games.Count(g =>
        g.Value.Achievements.Count == g.Value.UnlockedCount && g.Value.HasAchievements);
    private int PercentComplete
    {
        get
        {
            int total = _settingsVM.Games.Sum(g => g.Value.Achievements.Count);
            return  total == 0 ? 0 : Unlocked * 100 / total;
        }
    }

    public ProfileCard()
    {
        InitializeComponent();
        _settingsVM.PropertyChanged += SettingsVMOnPropertyChanged;
    }

    private void SettingsVMOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsVM.Games))
        {
            Bindings.Update();
        }
    }
}
