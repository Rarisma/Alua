using System.Text.Json;
using Alua.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Alua.UI;

public sealed partial class PSNLogin : Page
{
    private const string StoreUrl = "https://store.playstation.com/";
    private const string SsoCookieUrl = "https://ca.account.sony.com/api/v1/ssocookie";

    private TaskCompletionSource<string?> _resultTcs = new();

    // Null on Linux: Uno's X11 embedded WebView does not render reliably under XWayland, so Linux
    // uses the system browser + manual npsso paste and this control is never constructed.
    private WebView2? LoginWebView;

    /// <summary>
    /// The extracted NPSSO token, or null if cancelled.
    /// Await this after navigating to the page.
    /// </summary>
    public Task<string?> ResultTask => _resultTcs.Task;

    public PSNLogin()
    {
        InitializeComponent();

        if (!OperatingSystem.IsLinux())
        {
            LoginWebView = new WebView2();
            WebViewHost.Children.Add(LoginWebView);
            LoginWebView.Source = new Uri(StoreUrl);
        }
        else
        {
            // Linux: open PSN sign-in in the system browser; the user pastes the npsso token back.
            HeaderInstructions.Text =
                "Open PlayStation in your browser and sign in. Then open the SSO cookie page, copy the \"npsso\" value, paste it below, and click Done.";
            NpssoBox.Visibility = Visibility.Visible;

            var signInButton = new Button { Content = "Open PlayStation sign-in", HorizontalAlignment = HorizontalAlignment.Center };
            signInButton.Click += (_, _) => SystemBrowser.Open(StoreUrl);
            var cookieButton = new Button { Content = "Open SSO cookie page (after signing in)", HorizontalAlignment = HorizontalAlignment.Center };
            cookieButton.Click += (_, _) => SystemBrowser.Open(SsoCookieUrl);

            var panel = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(signInButton);
            panel.Children.Add(cookieButton);
            WebViewHost.Children.Add(panel);

            SystemBrowser.Open(StoreUrl);
        }
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

            // Linux: no embedded webview — take the token the user pasted (raw value or the
            // full {"npsso":"..."} JSON from the SSO cookie page).
            if (LoginWebView is null)
            {
                var token = ExtractNpssoFromInput(NpssoBox.Text);
                if (!string.IsNullOrEmpty(token))
                {
                    Log.Information("Accepted pasted NPSSO token");
                    _resultTcs.TrySetResult(token);
                    GoBack();
                }
                else
                {
                    Log.Warning("No valid NPSSO token pasted");
                }
                return;
            }

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

    /// <summary>
    /// Accepts either a bare npsso token or the full {"npsso":"..."} JSON the SSO cookie page
    /// returns, and normalises it to the bare token. Returns null if nothing usable was pasted.
    /// </summary>
    private static string? ExtractNpssoFromInput(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        if (text.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("npsso", out var el))
                    return el.GetString();
            }
            catch (JsonException)
            {
                // Not valid JSON — fall through and treat the input as a raw token.
            }
        }

        return text;
    }

    private async Task<string?> FetchNpssoAsync()
    {
        if (LoginWebView is null)
            return null;

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
