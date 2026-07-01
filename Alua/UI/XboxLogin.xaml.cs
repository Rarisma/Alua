using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Alua.UI;

/// <summary>
/// Hosts Microsoft/Xbox Live sign-in inside the app in an embedded WebView2. Started by
/// <see cref="Alua.Services.WebViewCustomWebUi"/>, which supplies MSAL's authorization URL and
/// redirect URI. When the WebView navigates to the redirect URI (carrying the auth code),
/// the page completes <see cref="ResultTask"/> with that URI; MSAL redeems the code.
/// </summary>
public sealed partial class XboxLogin : Page
{
    private readonly TaskCompletionSource<Uri?> _resultTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private WebView2? _loginWebView;
    private Uri? _redirectUri;

    /// <summary>
    /// Completes with the redirect URI (containing the auth code, or an error in its query),
    /// or null if the user cancelled. Await after calling <see cref="Start"/>.
    /// </summary>
    public Task<Uri?> ResultTask => _resultTcs.Task;

    public XboxLogin()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Navigates the embedded WebView to MSAL's authorization URL and begins watching for the
    /// redirect back to <paramref name="redirectUri"/>. Call on the UI thread.
    /// </summary>
    public void Start(Uri authorizationUri, Uri redirectUri)
    {
        _redirectUri = redirectUri;

        _loginWebView = new WebView2();
        _loginWebView.NavigationStarting += OnNavigationStarting;
        WebViewHost.Children.Add(_loginWebView);
        _loginWebView.Source = authorizationUri;
    }

    private void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (_redirectUri is null || string.IsNullOrEmpty(args.Uri))
            return;

        // The auth flow ends by redirecting to the loopback redirect URI (e.g.
        // http://localhost/?code=...). Nothing listens there, so cancel the doomed navigation
        // and hand the full URI to MSAL, which extracts and redeems the code.
        if (args.Uri.StartsWith(_redirectUri.GetLeftPart(UriPartial.Path), StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
            Log.Information("Xbox sign-in reached redirect URI; capturing result");
            _resultTcs.TrySetResult(new Uri(args.Uri));
            GoBack();
        }
    }

    private void OnCancel() => Cancel();

    /// <summary>Completes the flow as cancelled and navigates back. Call on the UI thread.</summary>
    public void Cancel()
    {
        Log.Information("Xbox sign-in cancelled");
        _resultTcs.TrySetResult(null);
        GoBack();
    }

    private void GoBack()
    {
        if (_loginWebView is not null)
            _loginWebView.NavigationStarting -= OnNavigationStarting;

        if (App.Frame.CanGoBack)
            App.Frame.GoBack();
    }
}
