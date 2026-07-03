namespace Alua.Models;

/// <summary>
/// Per-game scan progress reported by a provider: how many titles it has processed so far.
/// Consumed by the loading overlay to fill each provider's progress bar.
/// </summary>
public readonly record struct ScanProgress(int Current, int Total);
