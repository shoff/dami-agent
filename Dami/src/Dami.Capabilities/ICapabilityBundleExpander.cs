using Dami.Contracts.Context;

namespace Dami.Capabilities;

/// <summary>Expands selected capability identifiers into a turn-ready bundle.</summary>
public interface ICapabilityBundleExpander
{
    /// <summary>Expands selected skills and named bundles to their related capabilities.</summary>
    /// <param name="name">The resulting bundle name.</param>
    /// <param name="selectedCapabilityIds">The capabilities selected by retrieval.</param>
    /// <returns>A snapshot of selected and related capabilities.</returns>
    /// <param name="privacy">The privacy class restricting eligible capabilities.</param>
    CapabilityBundle Expand(
        string name,
        IReadOnlyList<Guid> selectedCapabilityIds,
        PrivacyClass privacy);
}
