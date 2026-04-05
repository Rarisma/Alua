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
            var loginPage = new PSNLogin();
            App.Frame.Navigate(typeof(PSNLogin));

            // Get the actual page instance from the frame
            if (App.Frame.Content is PSNLogin page)
            {
                return await page.ResultTask;
            }

            Log.Warning("Failed to get PSN login page instance");
            return null;
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
