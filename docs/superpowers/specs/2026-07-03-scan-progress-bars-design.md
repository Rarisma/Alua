# Scan progress bars — real per-game fill

**Date:** 2026-07-03
**Status:** Approved, pending implementation plan

## Problem

The per-provider progress-bar UI already exists in the scan/refresh loading overlay
(`Library.xaml` lines ~187–217), bound to `AppVM.ProviderScanStatus.Progress`. But the
bars are effectively binary: `Library.Scan()` / `Refresh()` set `Progress = 0.05` before a
provider starts and `Progress = 1.0` after it finishes, and never move in between.

Meanwhile the granular data already exists — every provider fetches games one at a time via
`RateLimitedExecutor.ExecuteAllWithNullableAsync`, which invokes a `(current, total)`
callback per completed game. Today all four providers wire that callback to a *single shared*
text line, `_appVm.LoadingGamesSummary` (e.g. `"Scanned Steam games (42/117)"`), instead of
into their own progress bar.

## Goal

Make each provider's bar fill smoothly as its games are scanned, by routing the per-game
`(current, total)` the provider already computes into its own `ProviderScanStatus.Progress`.

Scope: real per-game fill only. No new UI, no layout changes, no per-row counter redesign.

## Design

### 1. New value type — `Alua/Models/ScanProgress.cs`

```csharp
namespace Alua.Models;

/// <summary>Per-game scan progress: how many titles a provider has processed so far.</summary>
public readonly record struct ScanProgress(int Current, int Total);
```

### 2. `IAchievementProvider` — add an optional progress sink

```csharp
Task<Game[]> GetLibrary(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
Task<Game[]> RefreshLibrary(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
```

`RefreshTitle` is unchanged. `progress` is first so call sites read naturally, but see
"Call-site care" — existing positional `GetLibrary(ct)` calls must be updated.

### 3. Providers (Steam, PSN, Xbox, RetroAchievements)

Each provider has exactly one `RateLimitedExecutor.ExecuteAllWithNullableAsync` (or the
non-nullable variant) whose `(current, total)` callback currently writes
`_appVm.LoadingGamesSummary`. Change that callback to:

```csharp
(current, total) => progress?.Report(new ScanProgress(current, total))
```

- The up-front "Large PSN library (N titles), this may take several minutes…" warning on
  `LoadingGamesSummary` stays as-is — it is a one-shot warning, not per-game progress.
- Providers accept the new `progress` parameter and pass it through to the callback.

Affected files/lines (approximate, verify at implementation):
- `SteamService.cs` — `GetLibrary`, `RefreshLibrary`, `ConvertToAluaAsync` callback (~line 442)
- `PSNService.cs` — `GetLibraryCore`/`RefreshLibraryCore` callbacks (~lines 102, 161)
- `XboxService.cs` — `GetLibrary`/`RefreshLibrary` callback (~line 391)
- `RetroAchievementsService.cs` — `GetLibrary`/`RefreshLibrary` callbacks (~lines 85, 115)

### 4. `Library.Scan()` / `Refresh()` — build a per-provider reporter

For each provider in the sequential loop, construct a `Progress<ScanProgress>` bound to that
provider's status row and pass it into the call:

```csharp
var status = _appVm.ProviderScanStatuses[i];
status.Status   = "Scanning...";
status.Progress = 0.05; // seed so the bar is visible before the first game completes

var reporter = new Progress<ScanProgress>(p =>
{
    status.Progress = p.Total > 0 ? (double)p.Current / p.Total : 0.05;
    status.Status   = $"Scanning... ({p.Current}/{p.Total})";
});

var games = await provider.GetLibrary(reporter, ct);

status.GameCount = games.Length;
status.Status    = games.Length > 0 ? $"Found {games.Length} games" : "No games found";
status.Progress  = 1.0;
```

`Refresh()` uses the same pattern with "Refreshing…" / "Updated N games" wording.

### Why `Progress<T>` rather than a raw `Action`

`RateLimitedExecutor` invokes its callback from background worker threads (concurrency 3–5).
`Progress<T>` captures the UI `SynchronizationContext` at construction (Scan/Refresh run on
the UI thread) and marshals every `Report` back onto the UI thread, making the
`ObservableProperty` mutations thread-safe. This also fixes a latent defect: today the same
callbacks mutate `LoadingGamesSummary` off the UI thread.

### Call-site care

Making `progress` the first parameter means an unchanged `GetLibrary(ct)` would bind `ct` to
`progress`. The only caller of `GetLibrary`/`RefreshLibrary` is `Library.xaml.cs` (verified by
grep), and both call sites are edited in step 4, so this is contained.

## Non-goals (YAGNI)

- No new UI elements or layout changes — bars and rows already exist.
- No moving the shared `(x/y)` counter into a separate per-row element (that was a rejected
  larger-scope option).
- Providers still run sequentially; only one bar fills at a time. Unchanged.
- Steam/Xbox/RA fetch the game *list* in one API call before the per-game loop, so their bar
  sits at the 5% seed during that initial call, then fills. Acceptable.

## Testing

No test project exists in the solution. Verification is manual: build
`Alua/Alua.csproj -f net10.0-desktop`, run a scan and a refresh with at least one configured
provider, and confirm each provider's bar fills incrementally (not 5%→100%) and settles at
"Found N games".
