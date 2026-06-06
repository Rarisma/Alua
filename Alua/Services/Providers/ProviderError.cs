using System.Net;
using System.Net.Http;
using Sachya.PSN;

namespace Alua.Services.Providers;

/// <summary>How an achievement-provider failure should be presented and whether it's worth retrying.</summary>
public enum ProviderErrorKind
{
    /// Temporary (rate limit, 5xx, network) — retrying later may succeed.
    Transient,
    /// Authentication expired / unauthorized — the user must re-authenticate.
    AuthExpired,
    /// Permanent (bad request, not found, misconfiguration) — retrying won't help.
    Permanent
}

/// <summary>
/// Classifies provider exceptions into a kind + an actionable, user-facing message, so every
/// provider surfaces "re-authenticate" vs "try again" vs a specific failure consistently rather
/// than a single generic banner.
/// </summary>
public readonly record struct ProviderError(ProviderErrorKind Kind, string UserMessage)
{
    /// <summary>An "auth expired, sign in again" error for <paramref name="providerLabel"/>.</summary>
    public static ProviderError AuthExpired(string providerLabel) =>
        new(ProviderErrorKind.AuthExpired, $"{providerLabel}: Session expired. Please sign in again in Settings.");

    /// <summary>Classifies <paramref name="ex"/> for <paramref name="providerLabel"/> (e.g. "PlayStation").</summary>
    public static ProviderError Classify(string providerLabel, Exception ex)
    {
        var status = StatusOf(ex);

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return AuthExpired(providerLabel);

        bool transient = status == HttpStatusCode.TooManyRequests
            || (status.HasValue && (int)status.Value >= 500)
            || ex is HttpRequestException;
        if (transient)
            return new(ProviderErrorKind.Transient,
                $"{providerLabel}: Service is unavailable right now. Please try again shortly.");

        string reason = ex is InvalidOperationException ? "The item was not found." : "Check your settings and try again.";
        return new(ProviderErrorKind.Permanent, $"{providerLabel}: Unable to load data. {reason}");
    }

    private static HttpStatusCode? StatusOf(Exception ex) => ex switch
    {
        PlaystationApiException p => p.StatusCode,
        HttpRequestException h    => h.StatusCode,
        _                         => null
    };
}
