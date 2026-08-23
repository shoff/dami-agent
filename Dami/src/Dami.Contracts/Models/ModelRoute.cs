using Dami.Contracts.Context;

namespace Dami.Contracts.Models;

/// <summary>Where a piece of model work should run.</summary>
public enum ModelTier
{
    /// <summary>The loopback sidecar. The only tier local-only work may use.</summary>
    Local = 0,

    /// <summary>A frontier provider, reached through the egress boundary.</summary>
    Frontier = 1,
}

/// <summary>A routing decision, with the reason it was made.</summary>
public sealed record ModelRoute
{
    /// <summary>Creates a route.</summary>
    public ModelRoute(ModelTier tier, PrivacyClass privacy, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        this.Tier = tier;
        this.Privacy = privacy;
        this.Reason = reason;
    }

    /// <summary>The chosen tier.</summary>
    public ModelTier Tier { get; }

    /// <summary>The privacy class the decision was made under.</summary>
    public PrivacyClass Privacy { get; }

    /// <summary>Why, in one line — this appears in the execution event.</summary>
    public string Reason { get; }
}
