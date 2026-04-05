using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Alua.UI;

public sealed partial class PSNLogin : Page
{
    private const string StoreUrl = "https://store.playstation.com/";
    private const string SsoCookieUrl = "https://ca.account.sony.com/api/v1/ssocookie";

    private TaskCompletionSource<string?> _resultTcs = new();

    /// <summary>
    /// The extracted NPSSO token, or null if cancelled.
    /// Await this after navigating to the page.
    /// </summary>
    public Task<string?> ResultTask => _resultTcs.Task;

    public PSNLogin()
    {
        InitializeComponent();
        LoginWebView.Source = new Uri(StoreUrl);
    }

    private void OnCancel()
    {
        _resultTcs.TrySetResult(null);
        GoBack();
    }

    private async Task OnDone()
    {
        try
        {
            Progress.IsActive = true;
            Log.Information("User indicated PSN sign-in complete, fetching NPSSO token");

            // Navigate to ssocookie endpoint
            var npsso = await FetchNpssoAsync();

            if (!string.IsNullOrEmpty(npsso))
            {
                Log.Information("Successfully extracted NPSSO token");
                _resultTcs.TrySetResult(npsso);
                GoBack();
            }
            else
            {
                Log.Warning("Failed to extract NPSSO — user may not be signed in");
                // Navigate back to store so user can try signing in again
                LoginWebView.Source = new Uri(StoreUrl);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error extracting NPSSO token");
        }
        finally
        {
            Progress.IsActive = false;
        }
    }

    private async Task<string?> FetchNpssoAsync()
    {
        var navTcs = new TaskCompletionSource<bool>();

        async void OnNavCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            navTcs.TrySetResult(args.IsSuccess);
        }

        LoginWebView.NavigationCompleted += OnNavCompleted;
        LoginWebView.Source = new Uri(SsoCookieUrl);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() => navTcs.TrySetResult(false));
            var success = await navTcs.Task;

            if (!success)
                return null;

            var jsonResult = await LoginWebView.ExecuteScriptAsync("document.body.innerText");
            if (string.IsNullOrEmpty(jsonResult))
                return null;

            var bodyText = JsonSerializer.Deserialize<string>(jsonResult);
            if (string.IsNullOrEmpty(bodyText))
                return null;

            using var doc = JsonDocument.Parse(bodyText);
            if (doc.RootElement.TryGetProperty("npsso", out var npssoElement))
                return npssoElement.GetString();

            Log.Warning("ssocookie response: {Response}", bodyText);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching ssocookie");
            return null;
        }
        finally
        {
            LoginWebView.NavigationCompleted -= OnNavCompleted;
        }
    }

    private void GoBack()
    {
        if (App.Frame.CanGoBack)
            App.Frame.GoBack();
    }
}
