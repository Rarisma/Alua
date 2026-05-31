using Alua.UI;
using Sachya.PSN;
using Serilog;

namespace Alua.Services;

/// <summary>
/// Handles PlayStation Network authentication by navigating to a dedicated login page.
/// </summary>
public class PSNAuthService
{
    /// <summary>
    /// Opens the PSN login page in the main frame and returns the NPSSO token.
    /// User signs in on PlayStation Store, clicks Done, and we extract the token.
    /// </summary>
    /// <returns>NPSSO token string, or null if cancelled/failed</returns>
    public async Task<string?> AuthenticateAsync()
    {
        try
        {
            Log.Information("Navigating to PSN login page");

            PSNLogin? page = null;
            var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
            {
                App.Frame.Navigated -= OnNavigated;
                page = e.Content as PSNLogin;
                navigated.TrySetResult(page is not null);
            }

            App.Frame.Navigated += OnNavigated;
            var navigatedSuccessfully = App.Frame.Navigate(typeof(PSNLogin));

            if (!navigatedSuccessfully)
            {
                App.Frame.Navigated -= OnNavigated;
                Log.Warning("Frame.Navigate to PSNLogin returned false");
                return null;
            }

            // Wait for the Navigated event to fire, bounded by a timeout so a suppressed or
            // failed navigation (Navigated never raised) can't hang the auth flow — and its
            // spinner — forever.
            var completed = await Task.WhenAny(navigated.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != navigated.Task)
            {
                App.Frame.Navigated -= OnNavigated;
                Log.Warning("PSN login navigation did not complete within timeout");
                return null;
            }

            if (page is null)
            {
                Log.Warning("Failed to get PSN login page instance after navigation");
                return null;
            }

            return await page.ResultTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during PSN authentication");
            return null;
        }
    }

    /// <summary>
    /// Validates an NPSSO token by attempting to create a PSNClient.
    /// </summary>
    /// <returns>True if the token is valid</returns>
    public static async Task<bool> ValidateNpssoAsync(string npsso)
    {
        try
        {
            var client = await PSNClient.CreateFromNpsso(npsso);
            client.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NPSSO token validation failed");
            return false;
        }
    }
}
