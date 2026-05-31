namespace Alua.Models;

/// <summary>
/// String prefix constants used to build game identifiers for each provider.
/// All identifier strings take the form "&lt;prefix&gt;&lt;platform-specific-id&gt;".
/// </summary>
public static class ProviderIds
{
    /// <summary>Steam identifier prefix, e.g. "steam-570".</summary>
    public const string Steam = "steam-";

    /// <summary>Xbox identifier prefix, e.g. "xbox-1234567890".</summary>
    public const string Xbox = "xbox-";

    /// <summary>RetroAchievements identifier prefix, e.g. "ra-1234".</summary>
    public const string Retro = "ra-";

    /// <summary>PlayStation Network identifier prefix, e.g. "psn-NPWR12345_00".</summary>
    public const string PSN = "psn-";
}
