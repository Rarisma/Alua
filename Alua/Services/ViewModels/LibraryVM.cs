namespace Alua.Services.ViewModels;
public partial class LibraryVM : ObservableObject
{
    /// <summary>
    /// Are steam games shown
    /// </summary>
    [ObservableProperty] public bool _steamFilter = true;
    
    /// <summary>
    /// Are RetroAchievement games shown
    /// </summary>
    [ObservableProperty] public bool _RAFilter = true;
    
    /// <summary>
    /// Are PSN games shown
    /// </summary>
    [ObservableProperty] public bool _PSNFilter = true;
    
    /// <summary>
    /// Are Xbox Games shown?
    /// </summary>
    [ObservableProperty] public bool _XBFilter = true;

    
}
