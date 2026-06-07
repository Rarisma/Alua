// CachedImage.cs - Optimized cached image control with LRU memory + disk caching

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alua.UI.Controls;

public sealed class CachedImage : Image
{
    private const string CachePrefix = "imgcache_";
    private const long MaxCacheSizeBytes = 1L * 1024 * 1024 * 1024; // 1GB

    // Byte budgets for the in-process memory cache, expressed in *physical* decoded bytes
    // (BGRA, ~4 bytes/pixel). Entry sizes are accounted at the real decoded resolution
    // (decode size × display scale; see LoadImageAsync), so these limits reflect actual RAM.
    // Tunable: raising them caches more thumbnails (fewer re-decodes on scroll-back) at the
    // cost of more memory.
    private static readonly long MemoryCacheByteBudget = OperatingSystem.IsAndroid()
        ? 16L * 1024 * 1024   // 16 MB on Android
        : 32L * 1024 * 1024;  // 32 MB on Desktop / WASM

    private const int DefaultDecodeSize = 128;

    #region Dependency Properties

    public static readonly DependencyProperty UriSourceProperty =
        DependencyProperty.Register(
            nameof(UriSource),
            typeof(Uri),
            typeof(CachedImage),
            new PropertyMetadata(null, OnSourcePropertyChanged));

    public static readonly DependencyProperty DecodePixelWidthProperty =
        DependencyProperty.Register(
            nameof(DecodePixelWidth),
            typeof(int),
            typeof(CachedImage),
            new PropertyMetadata(0, OnSourcePropertyChanged));

    public static readonly DependencyProperty DecodePixelHeightProperty =
        DependencyProperty.Register(
            nameof(DecodePixelHeight),
            typeof(int),
            typeof(CachedImage),
            new PropertyMetadata(0, OnSourcePropertyChanged));

    public Uri? UriSource
    {
        get => (Uri?)GetValue(UriSourceProperty);
        set => SetValue(UriSourceProperty, value);
    }

    /// <summary>
    /// Decode width in pixels. Set this to the display size to reduce memory usage.
    /// If 0, uses element's ActualWidth or DefaultDecodeSize.
    /// </summary>
    public int DecodePixelWidth
    {
        get => (int)GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    /// <summary>
    /// Decode height in pixels. Set this to the display size to reduce memory usage.
    /// If 0, uses element's ActualHeight or DefaultDecodeSize.
    /// </summary>
    public int DecodePixelHeight
    {
        get => (int)GetValue(DecodePixelHeightProperty);
        set => SetValue(DecodePixelHeightProperty, value);
    }

    #endregion

    #region Static Caches

    private static readonly MemoryCache _memoryCache = new(new MemoryCacheOptions
    {
        SizeLimit = MemoryCacheByteBudget
    });

    // Separate cache for provider icons (small, reused constantly)
    private static readonly ConcurrentDictionary<string, BitmapImage> _providerCache = new();

    private static readonly ConcurrentDictionary<string, Task<string?>> _activeDownloads = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly SemaphoreSlim _downloadThrottle = new(OperatingSystem.IsAndroid() ? 2 : 3);
    private static int _cacheEnforcementScheduled;

    // Best-known display scale (RawPixelsPerViewPixel). Uno decodes Logical-typed bitmaps at
    // (decodeSize × scale), so cache entries must be accounted at that physical size or the byte
    // budget under-counts by scale² (≈4× on a 2× display) and the SizeLimit never evicts. Updated
    // from each element's XamlRoot; the last good value covers loads before an element is rooted.
    private static double _lastKnownScale = 1.0;

    #endregion

    private CancellationTokenSource? _cts;
    private string? _currentCacheKey;
    // Cheap pre-check fields — avoid SHA-256 when uri+decode-sizes are unchanged.
    private Uri? _currentUri;
    private int _lastDecodeW;
    private int _lastDecodeH;

    public CachedImage()
    {
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        // Release the bitmap so offscreen images can be GC'd
        Source = null;
        _currentCacheKey = null;
        _currentUri = null;
        _lastDecodeW = 0;
        _lastDecodeH = 0;
    }

    private static void OnSourcePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CachedImage img)
            img.LoadImage();
    }

    private async void LoadImage()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        var uri = UriSource;
        if (uri is null || !uri.IsAbsoluteUri)
        {
            Source = null;
            _cts = null;
            _currentCacheKey = null;
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var decodeWidth = GetDecodeWidth();
        var decodeHeight = GetDecodeHeight();

        // Cheap pre-check: if the URI and decode dimensions haven't changed, the image
        // is already loaded — skip the SHA-256 hash entirely.
        if (uri == _currentUri && decodeWidth == _lastDecodeW && decodeHeight == _lastDecodeH && _currentCacheKey is not null)
            return;

        var cacheKey = ComputeCacheKey(uri, decodeWidth, decodeHeight);

        // Skip if already displaying this image (belt-and-suspenders: handles hash collisions
        // and the case where only _currentUri was already updated without storing the key).
        if (_currentCacheKey == cacheKey)
            return;

        var scale = GetRasterizationScale();

        try
        {
            var bitmap = await LoadImageAsync(uri, cacheKey, decodeWidth, decodeHeight, scale, ct);
            if (!ct.IsCancellationRequested && bitmap is not null)
            {
                Source = bitmap;
                _currentCacheKey = cacheKey;
                _currentUri = uri;
                _lastDecodeW = decodeWidth;
                _lastDecodeH = decodeHeight;
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private int GetDecodeWidth()
    {
        if (DecodePixelWidth > 0) return DecodePixelWidth;
        if (ActualWidth > 0) return (int)Math.Ceiling(ActualWidth);
        if (Width > 0 && !double.IsNaN(Width)) return (int)Math.Ceiling(Width);
        return DefaultDecodeSize;
    }

    private int GetDecodeHeight()
    {
        if (DecodePixelHeight > 0) return DecodePixelHeight;
        if (ActualHeight > 0) return (int)Math.Ceiling(ActualHeight);
        if (Height > 0 && !double.IsNaN(Height)) return (int)Math.Ceiling(Height);
        return DefaultDecodeSize;
    }

    /// <summary>
    /// Display scale (RawPixelsPerViewPixel) for this element, used to account decoded bitmaps at
    /// their physical size. Remembers the last known value so loads that happen before the element
    /// is attached to a XamlRoot still estimate sensibly (falls back to 1.0 only on the very first).
    /// </summary>
    private double GetRasterizationScale()
    {
        var scale = XamlRoot?.RasterizationScale ?? 0;
        if (scale > 0)
            _lastKnownScale = scale;
        return _lastKnownScale;
    }

    private static async Task<BitmapImage?> LoadImageAsync(
        Uri uri, string cacheKey, int decodeWidth, int decodeHeight, double scale, CancellationToken ct)
    {
        // 1. Check provider cache (for frequently reused small icons)
        if (IsProviderIcon(uri) && _providerCache.TryGetValue(cacheKey, out var providerBitmap))
            return providerBitmap;

        // 2. Memory cache
        if (_memoryCache.TryGetValue(cacheKey, out BitmapImage? cached))
            return cached;

        ct.ThrowIfCancellationRequested();

        // 3. For ms-appx: URIs (local app resources), load directly via BitmapImage
        if (IsProviderIcon(uri))
        {
            var bitmap = new BitmapImage(uri)
            {
                DecodePixelWidth = decodeWidth,
                DecodePixelHeight = decodeHeight,
                DecodePixelType = DecodePixelType.Logical
            };
            _providerCache.TryAdd(cacheKey, bitmap);
            return bitmap;
        }

        // 4. Get or download file
        var localPath = await GetOrDownloadAsync(uri, cacheKey, ct);
        if (localPath is null)
            return null;

        ct.ThrowIfCancellationRequested();

        // 5. Load bitmap from stream with decode sizing
        var bitmap2 = await LoadBitmapFromFileAsync(localPath, decodeWidth, decodeHeight, ct);
        if (bitmap2 is null)
            return null;

        // 6. Cache in memory.
        // Account for the *physical* decoded size: Uno scales Logical decode targets by the
        // display scale, so the real bitmap is (decodeWidth×scale)×(decodeHeight×scale) BGRA.
        // Sizing by the unscaled dimensions under-counts by scale² and defeats the SizeLimit.
        var physicalWidth = (long)Math.Round(decodeWidth * scale);
        var physicalHeight = (long)Math.Round(decodeHeight * scale);
        var estimatedBytes = Math.Max(1L, physicalWidth * physicalHeight * 4);
        _memoryCache.Set(cacheKey, bitmap2, new MemoryCacheEntryOptions { Size = estimatedBytes });

        return bitmap2;
    }

    private static async Task<BitmapImage?> LoadBitmapFromFileAsync(
        string localPath, int decodeWidth, int decodeHeight, CancellationToken ct)
    {
        try
        {
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = decodeWidth,
                DecodePixelHeight = decodeHeight,
                DecodePixelType = DecodePixelType.Logical
            };

            ct.ThrowIfCancellationRequested();

            // Use file stream to avoid sync loading on UI thread
            await using var fileStream = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            using var memStream = new MemoryStream();
            await fileStream.CopyToAsync(memStream, ct);
            memStream.Position = 0;

            ct.ThrowIfCancellationRequested();

            await bitmap.SetSourceAsync(memStream.AsRandomAccessStream());
            return bitmap;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetOrDownloadAsync(Uri uri, string cacheKey, CancellationToken ct)
    {
        var folder = ApplicationData.Current.LocalFolder;
        var fileName = $"{CachePrefix}{cacheKey}{GetExtension(uri)}";
        var localPath = Path.Combine(folder.Path, fileName);

        // Already cached on disk
        if (File.Exists(localPath))
        {
            TouchFileAsync(localPath);
            return localPath;
        }

        // Deduplicate concurrent downloads for same URL
        var downloadTask = _activeDownloads.GetOrAdd(cacheKey, _ => DownloadWithThrottleAsync(uri, localPath, ct));

        try
        {
            return await downloadTask;
        }
        finally
        {
            _activeDownloads.TryRemove(cacheKey, out _);
        }
    }

    private static async Task<string?> DownloadWithThrottleAsync(Uri uri, string localPath, CancellationToken ct)
    {
        await _downloadThrottle.WaitAsync(ct);
        try
        {
            return await DownloadAsync(uri, localPath, ct);
        }
        finally
        {
            _downloadThrottle.Release();
        }
    }

    private static async Task<string?> DownloadAsync(Uri uri, string localPath, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            ct.ThrowIfCancellationRequested();

            var tempPath = localPath + ".tmp";
            await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                await httpStream.CopyToAsync(fileStream, ct);
            }

            // Atomic move
            File.Move(tempPath, localPath, overwrite: true);

            // Debounced cache limit enforcement — runs at most once per 5 seconds
            if (Interlocked.CompareExchange(ref _cacheEnforcementScheduled, 1, 0) == 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    try { await EnforceCacheLimitAsync(ApplicationData.Current.LocalFolder.Path); }
                    finally { Interlocked.Exchange(ref _cacheEnforcementScheduled, 0); }
                });
            }

            return localPath;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    private static void TouchFileAsync(string path)
    {
        Task.Run(() =>
        {
            try
            {
                // Only update if access time is older than 1 hour to reduce syscalls
                var info = new FileInfo(path);
                if ((DateTime.UtcNow - info.LastAccessTimeUtc).TotalHours >= 1)
                    File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            }
            catch { }
        });
    }

    /// <summary>
    /// Bound the on-disk cache to the configured size. Call once at app startup so a crash
    /// mid-scan doesn't leave the cache stuck over its limit until the next download.
    /// </summary>
    public static Task InitializeAsync() => EnforceCacheLimitAsync(ApplicationData.Current.LocalFolder.Path);

    private static async Task EnforceCacheLimitAsync(string folderPath)
    {
        try
        {
            await Task.Yield(); // Ensure we're off the UI thread

            var files = Directory.GetFiles(folderPath, $"{CachePrefix}*")
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastAccessTimeUtc)
                .ToList();

            var totalSize = files.Sum(f => f.Length);

            while (totalSize > MaxCacheSizeBytes && files.Count > 0)
            {
                var oldest = files[0];
                files.RemoveAt(0);
                totalSize -= oldest.Length;

                try { oldest.Delete(); }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Detects provider icons by scheme. All provider icons are local ms-appx assets;
    /// all game icons use https. This avoids false positives from Steam game icon URLs
    /// that contain "steam" in the path, which previously caused them to be permanently cached.
    /// </summary>
    private static bool IsProviderIcon(Uri uri)
    {
        return uri.Scheme == "ms-appx";
    }

    private static string GetExtension(Uri uri)
    {
        var ext = Path.GetExtension(uri.AbsolutePath);
        return string.IsNullOrEmpty(ext) || ext.Length > 5 ? ".png" : ext;
    }

    private static string ComputeCacheKey(Uri uri, int width, int height)
    {
        var input = $"{uri.AbsoluteUri}|{width}x{height}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..32]; // Truncate for shorter filenames
    }

    #region Public API

    /// <summary>
    /// Clears both memory and disk caches.
    /// </summary>
    public static async Task ClearCacheAsync()
    {
        _memoryCache.Compact(1.0);
        _providerCache.Clear();

        await Task.Run(() =>
        {
            var folder = ApplicationData.Current.LocalFolder;
            foreach (var file in Directory.GetFiles(folder.Path, $"{CachePrefix}*"))
            {
                try { File.Delete(file); }
                catch { }
            }
        });
    }

    /// <summary>
    /// Clears memory cache only. Useful when app is low on memory.
    /// </summary>
    public static void ClearMemoryCache()
    {
        _memoryCache.Compact(1.0);
        // Keep provider cache - it's small and constantly needed
    }

    /// <summary>
    /// Call from Android OnTrimMemory to release cached images under memory pressure.
    /// </summary>
    public static void OnLowMemory() => ClearMemoryCache();

    /// <summary>
    /// Preloads images into cache. Useful for prefetching visible items.
    /// </summary>
    public static async Task PreloadAsync(IEnumerable<Uri> uris, int decodeWidth = DefaultDecodeSize, int decodeHeight = DefaultDecodeSize, CancellationToken ct = default)
    {
        var tasks = uris.Select(uri =>
        {
            var cacheKey = ComputeCacheKey(uri, decodeWidth, decodeHeight);
            return LoadImageAsync(uri, cacheKey, decodeWidth, decodeHeight, _lastKnownScale, ct);
        });

        await Task.WhenAll(tasks);
    }

    #endregion
}
