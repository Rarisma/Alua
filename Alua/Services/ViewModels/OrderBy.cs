namespace Alua.Services.ViewModels;

/// <summary>
/// Sort modes for the library list. Persisted by name via <see cref="SettingsVM.OrderBy"/>, so
/// renaming a member is a breaking change for saved preferences; add new modes at the end.
/// </summary>
public enum OrderBy
{
    Name,
    NameReverse,
    CompletionPct,
    TotalCount,
    UnlockedCount,
    Playtime,
    LastPlayed,
    HowLongToBeatMain,
    HowLongToBeatCompletionist
}
