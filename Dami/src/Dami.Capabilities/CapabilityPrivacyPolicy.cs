using Dami.Contracts.Context;

namespace Dami.Capabilities;

/// <summary>Applies privacy eligibility consistently across capability retrieval and expansion.</summary>
internal static class CapabilityPrivacyPolicy
{
    /// <summary>Determines whether a capability can participate in a turn.</summary>
    public static bool Allows(CapabilityEntry capability, PrivacyClass privacy)
    {
        ArgumentNullException.ThrowIfNull(capability);
        EnsureDefined(privacy);

        return privacy != PrivacyClass.LocalOnly
            || capability.Source != CapabilitySource.Mcp
            || capability.Trust != TrustLevel.Untrusted;
    }

    /// <summary>Rejects unknown values so privacy policy fails closed.</summary>
    public static void EnsureDefined(PrivacyClass privacy)
    {
        if (!Enum.IsDefined(privacy))
        {
            throw new ArgumentOutOfRangeException(nameof(privacy), privacy, "Unknown privacy class.");
        }
    }
}
