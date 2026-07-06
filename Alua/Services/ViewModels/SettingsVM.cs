using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
//When you can't even say my name
namespace Alua.Services.ViewModels;
/// <summary>
/// Stores user data.
/// </summary>
public partial class SettingsVM  : ObservableObject
{
    private readonly object _gamesLock = new();
    [ObservableProperty]
    private Visibility _platformSettingsVisibility = Visibility.Visible;

    [ObservableProperty]
    private Visibility _uISettingsVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _metricsSettingsVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _debugSettingsVisibility = Visibility.Collapsed;
    // Serialization lock: prevents concurrent Save() calls from racing on the same .tmp file.
    private static readonly SemaphoreSlim _saveLock = new(1, 1);

    // Captured at construction time (on the UI thread) so background threads can marshal
    // Games property-change notifications back to the UI thread.
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _uiDispatcher;

    // Batch update support
    private int _batchUpdateDepth;
    private bool _gamesPendingNotification;
    private volatile bool _hasUnsavedChanges;
    
    #region Build info
    /// <summary>
    /// Shown in settings, and used to track when a full refresh is loaded
    /// </summary>
    [JsonInclude]
    public Version Version => Assembly.GetExecutingAssembly().GetName().Version ?? new();

    /// <summary>
    /// Shown under build number, enables debug mode.
    /// Generally a reference to a song.
    /// </summary>
    [JsonIgnore]
    public string BuildString = "Ball So Hard";

    /// <summary>
    /// Minimum settings-file version. Below this, MigrateSettings runs to bring the file forward.
    /// Bump this only when the settings format changes in a way an older client can't read.
    /// Pre-1.0 we wiped Games on bump; from 1.0+ we migrate instead — never silently drop user data.
    /// If a release truly needs scanned data refreshed, set Game.LastUpdated = DateTime.MinValue
    /// inside MigrateSettings so the next refresh refetches it.
    /// </summary>
    [JsonIgnore]
    public static Version MinimumVersion = new(0,3,0);
    #endregion

    #region Alua Data
    /// <summary>
    /// All games we have data for.
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("ScannedGames")]
    private Dictionary<string, Game> _games;
    
    /// <summary>
    /// Steam ID of user we are getting data for.
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("SteamUsername")]
    private string? _steamID;
    
    /// <summary>
    /// Steam ID of user we are getting data for.
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("RAUsername")]
    private string? _retroAchievementsUsername;

    /// <summary>
    /// PSN SSO token — secret, stored in SecureStorage rather than JSON.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string? _psnSSO;

    /// <summary>
    /// Microsoft authentication data — secret, stored in SecureStorage rather than JSON.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string? _microsoftAuthData;

    /// <summary>
    /// Xbox Live gamertag for display purposes
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("xboxGamertag")]
    private string? _xboxGamertag;

    /// <summary>
    /// User-provided Steam Web API key — secret, stored in SecureStorage rather than JSON.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string? _userSteamApiKey;

    /// <summary>
    /// User-provided RetroAchievements Web API key — secret, stored in SecureStorage rather than JSON.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string? _userRetroApiKey;

    /// <summary>
    /// True once legacy plaintext secrets have been migrated to SecureStorage.
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("migratedSecretsV1")]
    private bool _migratedSecretsV1;

    /// <summary>
    /// Captures unknown JSON keys at load time so legacy plaintext secrets can be migrated.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? LegacyExtras { get; set; }

    /// <summary>
    /// Controls if we show the first run dialog or game list
    /// </summary>
    [ObservableProperty, JsonInclude, JsonPropertyName("Init")]
    private bool _initialised;
    #endregion

    #region FilterData
    [ObservableProperty, JsonPropertyName("FilterHideComplete")]
    private bool _hideComplete;

    [ObservableProperty, JsonPropertyName("FilterHideNoAchievements")]
    private bool _hideNoAchievements;

    [ObservableProperty, JsonPropertyName("FilterHideUnstarted")]
    private bool _hideUnstarted;

    [ObservableProperty, JsonPropertyName("FilterReverse")]
    private bool _reverse;

    [ObservableProperty, JsonPropertyName("FilterOrderBy")]
    private OrderBy _orderBy = OrderBy.Name;

    [ObservableProperty, JsonPropertyName("FilterSingleColumnLayout")]
    private bool _singleColumnLayout;

    // ---- Appearance (card display) preferences. All save immediately from the Settings page. ----

    /// <summary>How achievement progress is drawn on a card. Replaces the legacy
    /// FilterFillBackgroundProgress bool, which is migrated on load (see LoadAsync).</summary>
    [ObservableProperty, JsonPropertyName("AppearanceCardProgressStyle")]
    private CardProgressStyle _cardProgressStyle = CardProgressStyle.Bar;

    /// <summary>Denser cards (smaller padding / icon) when true.</summary>
    [ObservableProperty, JsonPropertyName("AppearanceCompactCards")]
    private bool _compactCards;

    /// <summary>Whether the provider badge is shown on each card.</summary>
    [ObservableProperty, JsonPropertyName("AppearanceShowPlatformBadge")]
    private bool _showPlatformBadge = true;

    /// <summary>Opacity (0-1) of the provider badge on each card.</summary>
    [ObservableProperty, JsonPropertyName("AppearancePlatformBadgeOpacity")]
    private double _platformBadgeOpacity = 0.6;

    /// <summary>Whether playtime is shown on each card.</summary>
    [ObservableProperty, JsonPropertyName("AppearanceShowPlaytimeOnCard")]
    private bool _showPlaytimeOnCard = true;

    /// <summary>Horizontal alignment of card text.</summary>
    [ObservableProperty, JsonPropertyName("AppearanceCardTextAlignment")]
    private CardTextAlignment _cardTextAlignment = CardTextAlignment.Right;

    /// <summary>How a merged card reports completion (best edition vs aggregate across editions).</summary>
    [ObservableProperty, JsonPropertyName("AppearanceMergedCompletionMode")]
    private MergedCompletionMode _mergedCompletionMode = MergedCompletionMode.Best;

    // Per-platform visibility toggles. These mirror LibraryVM's bound toggles and default to
    // true (show everything) so existing settings files without these keys behave as before.
    [ObservableProperty, JsonPropertyName("FilterShowSteam")]
    private bool _steamFilter = true;

    [ObservableProperty, JsonPropertyName("FilterShowRetroAchievements")]
    private bool _RAFilter = true;

    [ObservableProperty, JsonPropertyName("FilterShowPlayStation")]
    private bool _PSNFilter = true;

    [ObservableProperty, JsonPropertyName("FilterShowXbox")]
    private bool _XBFilter = true;

    /// <summary>How duplicate / re-released editions are shown. Migrated from the legacy
    /// FilterMergeEditions bool on load — see LoadAsync.</summary>
    [ObservableProperty, JsonPropertyName("AppearanceEditionDisplayMode")]
    [NotifyPropertyChangedFor(nameof(IsMergeMode)), NotifyPropertyChangedFor(nameof(IsPriorityMode))]
    private EditionDisplayMode _editionDisplayMode = EditionDisplayMode.Merge;

    /// <summary>Platform order used by <see cref="EditionDisplayMode.PriorityOnly"/>; index 0 is
    /// shown first. Always exactly <see cref="SupportedPlatforms"/>, in some order — sanitized on
    /// load (see SanitizePlatformPriority).</summary>
    [ObservableProperty, JsonPropertyName("AppearancePlatformPriority")]
    private ObservableCollection<Platforms> _platformPriority = new(SupportedPlatforms);

    /// <summary>True when <see cref="MergedCompletionMode"/> has any effect (Merge mode only).</summary>
    public bool IsMergeMode => EditionDisplayMode == EditionDisplayMode.Merge;

    /// <summary>True when the platform priority list has any effect.</summary>
    public bool IsPriorityMode => EditionDisplayMode == EditionDisplayMode.PriorityOnly;

    /// <summary>The 4 platforms with a working provider, in default priority order. Mirrors the
    /// per-platform filter toggles (FilterShowSteam etc.) just above.</summary>
    private static readonly Platforms[] SupportedPlatforms =
        { Platforms.Steam, Platforms.PlayStation, Platforms.Xbox, Platforms.RetroAchievements };

    // When true, a game owned on multiple platforms / re-released across editions counts once in
    // aggregate statistics (no double-counting of totals or "perfect games"). Defaults to true.
    [ObservableProperty, JsonPropertyName("StatsCountMultiPlatformOnce")]
    private bool _countMultiPlatformOnce = true;

    /// <summary>
    /// Number of games to load per page (for infinite scroll pagination)
    /// </summary>
    [ObservableProperty, JsonPropertyName("PageSize")]
    private int _pageSize = 100;
    #endregion

    public SettingsVM()
    {
        _games = new();
        // Capture the UI thread's DispatcherQueue. SettingsVM is constructed on the UI thread
        // (via App.xaml.cs / DI), so GetForCurrentThread() returns the UI dispatcher.
        _uiDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    // Secrets are [JsonIgnore] and persisted only via SaveSecretsAsync inside Save(), which
    // early-returns when !_hasUnsavedChanges. Setting a secret must therefore mark the flag,
    // otherwise the new value is silently dropped (never written to SecureStorage) when no
    // game data changed in the same session. Note: load populates the backing fields directly
    // (not via these setters), so this does not cause a spurious save on startup.
    partial void OnPsnSSOChanged(string? value) => _hasUnsavedChanges = true;
    partial void OnMicrosoftAuthDataChanged(string? value) => _hasUnsavedChanges = true;
    partial void OnUserSteamApiKeyChanged(string? value) => _hasUnsavedChanges = true;
    partial void OnUserRetroApiKeyChanged(string? value) => _hasUnsavedChanges = true;

    // Filter/sort/layout preferences persist to JSON. Their [ObservableProperty] setters must mark
    // the VM dirty, otherwise Save() early-returns (no unsaved changes) and a toggled filter only
    // ever reached disk incidentally when a scan happened to dirty the VM in the same session.
    // The generated setters compare-before-set, so re-assigning the current value (e.g. restoring
    // the UI from the VM on startup) is a no-op and won't flag a spurious save. LoadAsync clears
    // the flag after deserialization so a pure load doesn't count as a change.
    partial void OnHideCompleteChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnHideNoAchievementsChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnHideUnstartedChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnReverseChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnOrderByChanged(OrderBy value) => _hasUnsavedChanges = true;
    partial void OnSingleColumnLayoutChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnCardProgressStyleChanged(CardProgressStyle value) => _hasUnsavedChanges = true;
    partial void OnCompactCardsChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnShowPlatformBadgeChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnPlatformBadgeOpacityChanged(double value) => _hasUnsavedChanges = true;
    partial void OnShowPlaytimeOnCardChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnCardTextAlignmentChanged(CardTextAlignment value) => _hasUnsavedChanges = true;
    partial void OnMergedCompletionModeChanged(MergedCompletionMode value) => _hasUnsavedChanges = true;
    partial void OnSteamFilterChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnRAFilterChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnPSNFilterChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnXBFilterChanged(bool value) => _hasUnsavedChanges = true;
    partial void OnEditionDisplayModeChanged(EditionDisplayMode value) => _hasUnsavedChanges = true;
    partial void OnPlatformPriorityChanged(ObservableCollection<Platforms> value) => _hasUnsavedChanges = true;
    partial void OnCountMultiPlatformOnceChanged(bool value) => _hasUnsavedChanges = true;

    /// <summary>
    /// Begins a batch update scope. Notifications are deferred until all scopes complete.
    /// </summary>
    public IDisposable BeginBatchUpdate()
    {
        _batchUpdateDepth++;
        return new BatchUpdateScope(this);
    }

    private void EndBatchUpdate()
    {
        _batchUpdateDepth--;
        if (_batchUpdateDepth == 0 && _gamesPendingNotification)
        {
            _gamesPendingNotification = false;
            // A batch may be ended on a background thread (e.g. the HLTB fetch resumes off-UI);
            // marshal the notification so downstream binding updates run on the UI thread.
            if (_uiDispatcher != null)
                _uiDispatcher.TryEnqueue(() => OnPropertyChanged(nameof(Games)));
            else
                OnPropertyChanged(nameof(Games));
        }
    }

    /// <summary>
    /// Ensures LastPlayed is populated, falling back to the most recent achievement unlock
    /// or preserving the existing value when the provider doesn't supply one.
    /// </summary>
    private void ResolveLastPlayed(Game game)
    {
        if (game.LastPlayed == null && game.Achievements.Count > 0)
        {
            game.LastPlayed = game.Achievements
                .Where(a => a.UnlockedOn.HasValue)
                .Select(a => a.UnlockedOn!.Value)
                .DefaultIfEmpty()
                .Max();

            // DefaultIfEmpty returns DateTime.MinValue when empty; treat as null
            if (game.LastPlayed == DateTime.MinValue)
                game.LastPlayed = null;
        }

        // Preserve existing LastPlayed if the new data doesn't have one
        if (game.LastPlayed == null && Games.TryGetValue(game.Identifier, out var existing))
        {
            game.LastPlayed = existing.LastPlayed;
        }
    }

    /// <summary>
    /// Adds or updates a game in the collection and notifies listeners.
    /// </summary>
    /// <param name="game">Game to add or update.</param>
    public void AddOrUpdateGame(Game game)
    {
        lock (_gamesLock)
        {
            ResolveLastPlayed(game);

            if (Games.TryGetValue(game.Identifier, out var existing))
            {
                // Never let an incoming game with no icon overwrite a previously-good one. A provider
                // refresh can legitimately return a game with an empty Icon (e.g. RA's recently-played
                // payload); without this guard that wipes the icon we already scanned, leaving the card
                // blank until a full rescan.
                if (string.IsNullOrWhiteSpace(game.Icon) && !string.IsNullOrWhiteSpace(existing.Icon))
                {
                    game.Icon = existing.Icon;
                }

                // Providers never populate HLTB fields themselves (a separate fetch phase does),
                // so a freshly-scanned game always has them null. Without carrying the old values
                // forward here, the 7-day "already fetched" check in
                // HowLongToBeatService.FetchAndUpdateGameData would never see a real timestamp and
                // every scan/refresh would re-query HLTB for every game.
                game.HowLongToBeatMain = existing.HowLongToBeatMain;
                game.HowLongToBeatMainExtras = existing.HowLongToBeatMainExtras;
                game.HowLongToBeatCompletionist = existing.HowLongToBeatCompletionist;
                game.HowLongToBeatAllStyles = existing.HowLongToBeatAllStyles;
                game.HowLongToBeatLastFetched = existing.HowLongToBeatLastFetched;
            }

            Games[game.Identifier] = game;
            _hasUnsavedChanges = true;
        }

        if (_batchUpdateDepth > 0)
        {
            _gamesPendingNotification = true;
        }
        else
        {
            // HLTB and scan threads call this from background threads; marshal notification to the UI thread.
            if (_uiDispatcher != null)
                _uiDispatcher.TryEnqueue(() => OnPropertyChanged(nameof(Games)));
            else
                OnPropertyChanged(nameof(Games));
        }
    }

    private sealed class BatchUpdateScope : IDisposable
    {
        private readonly SettingsVM _vm;
        private bool _disposed;

        public BatchUpdateScope(SettingsVM vm) => _vm = vm;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _vm.EndBatchUpdate();
            }
        }
    }


    /// <summary>
    /// Saves settings to disk. Only writes if there are unsaved changes.
    /// Uses stream-based serialization with atomic write (temp file + move) to reduce memory usage.
    /// Concurrent callers are serialized via _saveLock to prevent races on the .tmp file.
    /// </summary>
    /// <param name="force">If true, saves even if no changes detected.</param>
    public async Task Save(bool force = false)
    {
        if (!force && !_hasUnsavedChanges)
        {
            Log.Debug("Skipping save - no unsaved changes detected.");
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            // Re-check after acquiring the lock — a concurrent save may have already flushed.
            if (!force && !_hasUnsavedChanges)
            {
                Log.Debug("Skipping save - changes already flushed by concurrent save.");
                return;
            }

            var folderPath = ApplicationData.Current.LocalFolder.Path;
            var filePath = Path.Combine(folderPath, "Settings.json");
            var tempPath = filePath + ".tmp";

            Log.Information($"Saving settings to {filePath}");

            // Create a copy of the Games dictionary to avoid collection modification during serialization
            Dictionary<string, Game> gamesCopy;
            lock (_gamesLock)
            {
                gamesCopy = new Dictionary<string, Game>(Games);
            }

            // Snapshot the non-secret state for serialization. Secret fields are intentionally
            // excluded — they go to SecureStorage below.
            var settingsCopy = CloneForSerialization(gamesCopy);

            // Persist secrets via SecureStorage.
            await SaveSecretsAsync();

            // Stream-based serialization to temp file to avoid materializing full JSON string
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settingsCopy);
            }

            // Atomic move to prevent corruption on crash
            File.Move(tempPath, filePath, overwrite: true);
            _hasUnsavedChanges = false;

            Log.Information("Saved settings.");
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to save settings");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Serializes current settings (without secrets) to a JSON string suitable for backup or transfer.
    /// </summary>
    public Task<string> ExportToJsonAsync()
    {
        Dictionary<string, Game> gamesCopy;
        lock (_gamesLock)
        {
            gamesCopy = new Dictionary<string, Game>(Games);
        }

        // CloneForSerialization copies only non-secret state, so the secret fields are never
        // populated on the clone (and are [JsonIgnore] regardless).
        var sanitized = CloneForSerialization(gamesCopy);

        var options = new JsonSerializerOptions { WriteIndented = true };
        return Task.FromResult(JsonSerializer.Serialize(sanitized, options));
    }

    /// <summary>
    /// Builds a throwaway copy of this view-model holding only the non-secret, persistable state,
    /// shared by <see cref="Save"/> and <see cref="ExportToJsonAsync"/>. Secret fields (NPSSO,
    /// MSAL cache, API keys) are deliberately excluded — they live in SecureStorage.
    /// </summary>
    private SettingsVM CloneForSerialization(Dictionary<string, Game> gamesCopy)
    {
        // Direct backing-field copies intentionally snapshot state onto a throwaway object
        // without raising change notifications; some of these fields are [ObservableProperty].
#pragma warning disable MVVMTK0034
        return new SettingsVM
        {
            _games = gamesCopy,
            _steamID = _steamID,
            _retroAchievementsUsername = _retroAchievementsUsername,
            _xboxGamertag = _xboxGamertag,
            _migratedSecretsV1 = _migratedSecretsV1,
            _initialised = _initialised,
            _hideComplete = _hideComplete,
            _hideNoAchievements = _hideNoAchievements,
            _hideUnstarted = _hideUnstarted,
            _reverse = _reverse,
            _orderBy = _orderBy,
            _singleColumnLayout = _singleColumnLayout,
            _cardProgressStyle = _cardProgressStyle,
            _compactCards = _compactCards,
            _showPlatformBadge = _showPlatformBadge,
            _platformBadgeOpacity = _platformBadgeOpacity,
            _showPlaytimeOnCard = _showPlaytimeOnCard,
            _cardTextAlignment = _cardTextAlignment,
            _mergedCompletionMode = _mergedCompletionMode,
            _steamFilter = _steamFilter,
            _RAFilter = _RAFilter,
            _PSNFilter = _PSNFilter,
            _XBFilter = _XBFilter,
            _editionDisplayMode = _editionDisplayMode,
            _platformPriority = new ObservableCollection<Platforms>(_platformPriority),
            _countMultiPlatformOnce = _countMultiPlatformOnce,
            _pageSize = _pageSize
        };
#pragma warning restore MVVMTK0034
    }

    // 50 MB: a generous upper bound for a settings file; protects against untrusted/malformed imports.
    private const int ImportMaxBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Replaces game data and filter preferences from an exported JSON blob.
    /// Does NOT touch authentication / API key fields — those stay in SecureStorage.
    /// </summary>
    public async Task ImportFromJsonAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Import payload is empty", nameof(json));

        if (System.Text.Encoding.UTF8.GetByteCount(json) > ImportMaxBytes)
            throw new InvalidOperationException($"Import payload exceeds the {ImportMaxBytes / 1024 / 1024} MB limit and was rejected.");

        SettingsVM imported;
        try
        {
            imported = JsonSerializer.Deserialize<SettingsVM>(json)
                       ?? throw new InvalidOperationException("Failed to parse imported settings");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Import payload is not valid settings JSON.", ex);
        }

        if (imported.Version > Version)
            Log.Warning("Importing settings authored by a newer Alua ({Imported}); some fields may be ignored", imported.Version);

        using (BeginBatchUpdate())
        {
            lock (_gamesLock)
            {
                Games = imported.Games ?? new Dictionary<string, Game>();
            }
            _gamesPendingNotification = true;

            SteamID = imported.SteamID;
            RetroAchievementsUsername = imported.RetroAchievementsUsername;
            HideComplete = imported.HideComplete;
            HideNoAchievements = imported.HideNoAchievements;
            HideUnstarted = imported.HideUnstarted;
            Reverse = imported.Reverse;
            OrderBy = imported.OrderBy;
            SingleColumnLayout = imported.SingleColumnLayout;
            SteamFilter = imported.SteamFilter;
            RAFilter = imported.RAFilter;
            PSNFilter = imported.PSNFilter;
            XBFilter = imported.XBFilter;
            if (imported.EditionDisplayMode == EditionDisplayMode.Merge
                && imported.LegacyExtras != null
                && imported.LegacyExtras.TryGetValue("FilterMergeEditions", out var legacyMerge))
            {
                imported.EditionDisplayMode = legacyMerge.ValueKind == System.Text.Json.JsonValueKind.True
                    ? EditionDisplayMode.Merge
                    : EditionDisplayMode.DontMerge;
            }
            EditionDisplayMode = imported.EditionDisplayMode;
            PlatformPriority = new ObservableCollection<Platforms>(imported.PlatformPriority);
            SanitizePlatformPriority();
            PageSize = imported.PageSize;
            _hasUnsavedChanges = true;
        }

        await Save(force: true);
    }

    /// <summary>
    /// Secure-storage key names for sensitive fields.
    /// </summary>
    private const string SecretPsnSso = "psn_sso";
    private const string SecretMicrosoftAuth = "microsoft_auth_data";
    private const string SecretSteamApiKey = "user_steam_api_key";
    private const string SecretRetroApiKey = "user_retro_api_key";

    private async Task MigrateAndLoadSecretsAsync()
    {
        // Migrate legacy plaintext secrets (if present in the old JSON) into SecureStorage.
        if (!MigratedSecretsV1 && LegacyExtras is not null)
        {
            string? Pop(string key)
            {
                if (LegacyExtras.TryGetValue(key, out var element) &&
                    element.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    LegacyExtras.Remove(key);
                    return element.GetString();
                }
                return null;
            }

            var legacyPsn = Pop("psnsso");
            var legacyMs = Pop("microsoftAuthData");
            var legacySteam = Pop("userSteamApiKey");
            var legacyRetro = Pop("userRetroApiKey");

            if (legacyPsn is not null) await Services.SecureStorage.SetAsync(SecretPsnSso, legacyPsn);
            if (legacyMs is not null) await Services.SecureStorage.SetAsync(SecretMicrosoftAuth, legacyMs);
            if (legacySteam is not null) await Services.SecureStorage.SetAsync(SecretSteamApiKey, legacySteam);
            if (legacyRetro is not null) await Services.SecureStorage.SetAsync(SecretRetroApiKey, legacyRetro);

            MigratedSecretsV1 = true;
            _hasUnsavedChanges = true;
            Log.Information("Migrated legacy plaintext secrets to SecureStorage");

            // Also clean up the old MSAL token cache file — its contents are now expected
            // to be re-acquired through the secure flow.
            try
            {
                var legacyCachePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "msal_token_cache.bin");
                if (File.Exists(legacyCachePath))
                {
                    File.Delete(legacyCachePath);
                    Log.Information("Removed legacy MSAL token cache file");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete legacy MSAL token cache");
            }
        }

        // Populate secret fields from SecureStorage. Read directly into backing fields
        // to avoid bouncing through change notifications.
#pragma warning disable MVVMTK0034
        _psnSSO = await Services.SecureStorage.GetAsync(SecretPsnSso);
        _microsoftAuthData = await Services.SecureStorage.GetAsync(SecretMicrosoftAuth);
        _userSteamApiKey = await Services.SecureStorage.GetAsync(SecretSteamApiKey);
        _userRetroApiKey = await Services.SecureStorage.GetAsync(SecretRetroApiKey);
#pragma warning restore MVVMTK0034
    }

    private async Task SaveSecretsAsync()
    {
        // Read backing fields directly; these are write-throughs to SecureStorage, not UI state.
#pragma warning disable MVVMTK0034
        await Services.SecureStorage.SetAsync(SecretPsnSso, _psnSSO);
        await Services.SecureStorage.SetAsync(SecretMicrosoftAuth, _microsoftAuthData);
        await Services.SecureStorage.SetAsync(SecretSteamApiKey, _userSteamApiKey);
        await Services.SecureStorage.SetAsync(SecretRetroApiKey, _userRetroApiKey);
#pragma warning restore MVVMTK0034
    }

    /// <summary>
    /// Applies version-specific migrations when loading a settings file older than MinimumVersion.
    /// Adds new fields, renames JSON keys, or marks games for re-fetch. Does NOT wipe Games.
    /// Each release that needs migration should add a branch here keyed on fromVersion.
    /// </summary>
    private static void MigrateSettings(SettingsVM model, Version fromVersion)
    {
        // Pre-1.0 wiped Games on every bump; from 1.0+ we preserve user data.
        // If a release needs scanned data refreshed, invalidate per-game LastUpdated here.
        Log.Information("No migration steps registered for {From} -> {To}", fromVersion, MinimumVersion);
    }

    /// <summary>
    /// The <see cref="ProviderIds"/> identifier prefix for a platform, or null if it has none.
    /// </summary>
    private static string? IdentifierPrefixFor(Platforms platform) => platform switch
    {
        Platforms.Steam             => ProviderIds.Steam,
        Platforms.Xbox              => ProviderIds.Xbox,
        Platforms.RetroAchievements => ProviderIds.Retro,
        Platforms.PlayStation       => ProviderIds.PSN,
        _                           => null
    };

    /// <summary>
    /// Re-keys any persisted game stored under a bare (unprefixed) identifier to its
    /// "&lt;prefix&gt;&lt;id&gt;" form (see <see cref="ProviderIds"/>). Older settings files stored some
    /// providers — notably PlayStation — under the raw platform id; without this, a refresh would
    /// add a second entry under the new prefixed key and the library would show duplicate cards.
    /// Idempotent: games already prefixed are skipped, and a stale bare entry is dropped if a
    /// prefixed entry already exists.
    /// </summary>
    private void NormalizeGameIdentifiers()
    {
        lock (_gamesLock)
        {
            foreach (var oldKey in Games.Keys.ToList())
            {
                var game = Games[oldKey];
                var prefix = IdentifierPrefixFor(game.Platform);
                if (prefix is null || oldKey.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var newKey = prefix + oldKey;
                Games.Remove(oldKey);

                // Keep a pre-existing prefixed entry (it is the newer one); otherwise re-key.
                if (!Games.ContainsKey(newKey))
                {
                    game.Identifier = newKey;
                    Games[newKey] = game;
                }

                _hasUnsavedChanges = true;
                Log.Information("Migrated game identifier {Old} -> {New}", oldKey, newKey);
            }
        }
    }

    /// <summary>
    /// De-duplicates and completes <see cref="PlatformPriority"/> so it always holds exactly the
    /// 4 supported platforms, in some order — covers settings files that predate this feature (no
    /// key at all) and any future platform added to <see cref="SupportedPlatforms"/>.
    /// </summary>
    private void SanitizePlatformPriority()
    {
        var seen = new HashSet<Platforms>();
        var sanitized = new List<Platforms>();
        foreach (var p in PlatformPriority)
            if (SupportedPlatforms.Contains(p) && seen.Add(p)) sanitized.Add(p);
        foreach (var p in SupportedPlatforms)
            if (seen.Add(p)) sanitized.Add(p);

        if (!sanitized.SequenceEqual(PlatformPriority))
        {
            PlatformPriority = new ObservableCollection<Platforms>(sanitized);
            _hasUnsavedChanges = true;
        }
    }

    ///<summary>
    /// Reads settings from disk using stream-based deserialization to reduce memory usage.
    /// </summary>
    public static async Task<SettingsVM> LoadAsync()
    {
        try
        {
            string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Settings.json");
            Log.Information("Loading settings from path");
            if (File.Exists(path))
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
                if (stream.Length == 0)
                {
                    Log.Warning("Settings file exists but is empty, returning default settings.");
                    return new SettingsVM();
                }

                SettingsVM Model = (await JsonSerializer.DeserializeAsync<SettingsVM>(stream))!;

                // Deserialization runs through the [ObservableProperty] setters, whose OnChanged
                // hooks flip _hasUnsavedChanges. That's just load noise — clear it so a pure load
                // doesn't trigger a redundant full save. The migrations below set it again only if
                // they actually change something.
                Model._hasUnsavedChanges = false;

                // Migrate the legacy fill-background bool to the new progress-style enum. The old
                // "FilterFillBackgroundProgress" key lands in LegacyExtras now that the property is
                // gone; an old save with it set to true becomes FilledBackground.
                if (Model.CardProgressStyle == CardProgressStyle.Bar
                    && Model.LegacyExtras != null
                    && Model.LegacyExtras.TryGetValue("FilterFillBackgroundProgress", out var fillBg)
                    && fillBg.ValueKind == System.Text.Json.JsonValueKind.True)
                {
                    Model.CardProgressStyle = CardProgressStyle.FilledBackground;
                    Model.LegacyExtras.Remove("FilterFillBackgroundProgress");
                }

                // Migrate the legacy edition-merge bool to the new 3-way display mode. The old
                // "FilterMergeEditions" key lands in LegacyExtras now that the bool property is gone.
                if (Model.EditionDisplayMode == EditionDisplayMode.Merge
                    && Model.LegacyExtras != null
                    && Model.LegacyExtras.TryGetValue("FilterMergeEditions", out var legacyMerge))
                {
                    Model.EditionDisplayMode = legacyMerge.ValueKind == System.Text.Json.JsonValueKind.True
                        ? EditionDisplayMode.Merge
                        : EditionDisplayMode.DontMerge;
                    Model.LegacyExtras.Remove("FilterMergeEditions");
                }

                if (MinimumVersion > Model.Version)
                {
                    Log.Information("Migrating settings from {OldVersion} to {NewVersion}", Model.Version, MinimumVersion);
                    MigrateSettings(Model, Model.Version);
                }
                else {Log.Information($"Loaded settings file from version {Model.Version.ToString()}");}

                await Model.MigrateAndLoadSecretsAsync();

                // Re-key any legacy bare identifiers (e.g. pre-prefix PlayStation games) so a
                // later refresh doesn't create duplicate library entries under the new prefixed key.
                Model.NormalizeGameIdentifiers();

                Model.SanitizePlatformPriority();

                return Model;
            }

            Log.Information("Settings file not found.");
            return new SettingsVM();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not load existing settings file");
            return new SettingsVM();
        }
    }

}
