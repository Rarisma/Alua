// CachedImage.cs  –  drop into any shared project

using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Alua.UI.Controls;

public sealed class CachedImage : Image
{
    public static readonly DependencyProperty UriSourceProperty =
        DependencyProperty.Register(
            nameof(UriSource),
            typeof(Uri),
            typeof(CachedImage),
            new PropertyMetadata(null, OnUriChanged));

    public Uri UriSource
    {
        get => (Uri)GetValue(UriSourceProperty);
        set => SetValue(UriSourceProperty, value);
    }

    // shared across all instances
    private static readonly HttpClient _http = new();
    private static readonly SemaphoreSlim _gate = new(1, 1);

    private static async void OnUriChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CachedImage img || e.NewValue is not Uri uri || !uri.IsAbsoluteUri)
            return;

        var folder   = ApplicationData.Current.LocalFolder;           // never auto‑cleaned
        var fileName = Hash(uri) + Path.GetExtension(uri.AbsolutePath);

        StorageFile file;
        if (!await FileExists(folder, fileName))
        {
            await _gate.WaitAsync();            // serialise first‑time fetches
            try
            {
                if (!await FileExists(folder, fileName))
                {
                    var bytes = await _http.GetByteArrayAsync(uri);
                    file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteBytesAsync(file, bytes);
                }
            }
            finally { _gate.Release(); }
        }

        var localUri = new Uri($"ms-appdata:///local/{fileName}");
        img.Source   = new BitmapImage(localUri);
    }

    // ---------- helpers ----------

    public static async Task ClearCacheAsync()                // call when you really want to wipe the cache
    {
        var files = await ApplicationData.Current.LocalFolder.GetFilesAsync();
        foreach (var f in files) await f.DeleteAsync();
    }

    private static async Task<bool> FileExists(StorageFolder f, string name)
    {
        try { await f.GetFileAsync(name); return true; }
        catch { return false; }
    }

    private static string Hash(Uri u)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(u.AbsoluteUri));
        return Convert.ToHexString(bytes);                     // fast, unique, filesystem‑safe
    }
}
