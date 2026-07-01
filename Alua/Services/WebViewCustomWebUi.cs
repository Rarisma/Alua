using Alua.UI;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensibility;
using Microsoft.UI.Xaml.Navigation;
using Serilog;

namespace Alua.Services;

/// <summary>
/// MSAL custom web UI that hosts interactive sign-in in the app's own WebView2
/// (<see cref="XboxLogin"/>) instead of the system browser. MSAL still performs the PKCE
/// authorization-code -> token exchange; this only supplies the UI surface. Used on
/// Windows/macOS desktop only (gated in <see cref="MicrosoftAuthService"/>).
/// </summary>
public class WebViewCustomWebUi : ICustomWebUi
{
    public Task<Uri> AcquireAuthorizationCodeAsync(
        Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);

        // MSAL may invoke this off the UI thread; all Frame/WebView work must be marshalled.
        var dispatcher = App.Frame.DispatcherQueue;
        bool enqueued = dispatcher.TryEnqueue(async () =>
        {
            try
            {
                var page = await NavigateToLoginPageAsync();
                if (page is null)
                {
                    tcs.TrySetException(new MsalClientException(
                        MsalError.AuthenticationCanceledError, "Could not open the Xbox sign-in page."));
                    return;
                }

                page.Start(authorizationUri, redirectUri);

                // If MSAL cancels, tear the page down on the UI thread.
                using var registration = cancellationToken.Register(
                    () => dispatcher.TryEnqueue(() => page.Cancel()));

                Uri? result = await page.ResultTask;
                if (result is null)
                {
                    tcs.TrySetException(new MsalClientException(
                        MsalError.AuthenticationCanceledError, "Xbox sign-in was cancelled."));
                }
                else
                {
                    tcs.TrySetResult(result);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Xbox custom web UI failed");
                tcs.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            tcs.TrySetException(new MsalClientException(
                MsalError.AuthenticationCanceledError, "UI thread is unavailable for Xbox sign-in."));
        }

        return tcs.Task;
    }

    /// <summary>
    /// Navigates the app frame to <see cref="XboxLogin"/> and returns the page instance,
    /// mirroring the navigation-capture pattern in <see cref="PSNAuthService"/>.
    /// Must run on the UI thread.
    /// </summary>
    private static async Task<XboxLogin?> NavigateToLoginPageAsync()
    {
        XboxLogin? page = null;
        var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigated(object sender, NavigationEventArgs e)
        {
            App.Frame.Navigated -= OnNavigated;
            page = e.Content as XboxLogin;
            navigated.TrySetResult(page is not null);
        }

        App.Frame.Navigated += OnNavigated;
        if (!App.Frame.Navigate(typeof(XboxLogin)))
        {
            App.Frame.Navigated -= OnNavigated;
            Log.Warning("Frame.Navigate to XboxLogin returned false");
            return null;
        }

        // Bound the wait so a suppressed/failed navigation can't hang the auth flow forever.
        var completed = await Task.WhenAny(navigated.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        if (completed != navigated.Task)
        {
            App.Frame.Navigated -= OnNavigated;
            Log.Warning("Xbox login navigation did not complete within timeout");
            return null;
        }

        return page;
    }
}
