using Dami.Contracts.Context;

namespace Dami.Capabilities;

/// <summary>Resolves related capabilities through a source-neutral catalog.</summary>
public sealed class CapabilityBundleExpander : ICapabilityBundleExpander
{
    private readonly ICapabilityCatalog catalog;

    /// <summary>Initializes a capability bundle expander.</summary>
    /// <param name="catalog">The source-neutral capability lookup surface.</param>
    public CapabilityBundleExpander(ICapabilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
    }

    /// <inheritdoc />
    public CapabilityBundle Expand(
        string name,
        IReadOnlyList<Guid> selectedCapabilityIds,
        PrivacyClass privacy)
    {
        var capabilities = new List<CapabilityEntry>();
        var includedCapabilityIds = new HashSet<Guid>();
        var pendingCapabilityIds = CreatePending(selectedCapabilityIds, privacy);

        while (pendingCapabilityIds.TryPop(out var pendingCapability))
        {
            if (!includedCapabilityIds.Add(pendingCapability.CapabilityId))
            {
                continue;
            }

            if (this.ResolveEligible(pendingCapability, privacy) is not { } capability)
            {
                continue;
            }

            if (capability.Kind != CapabilityKind.Bundle)
            {
                capabilities.Add(capability);
            }

            if (capability.Kind == CapabilityKind.Tool)
            {
                continue;
            }

            PushRelated(capability, pendingCapabilityIds);
        }

        return new CapabilityBundle(name, capabilities);
    }

    private CapabilityEntry? ResolveEligible(
        PendingCapability pendingCapability,
        PrivacyClass privacy)
    {
        var capability = this.Resolve(pendingCapability);
        return CapabilityPrivacyPolicy.Allows(capability, privacy) ? capability : null;
    }

    private CapabilityEntry Resolve(PendingCapability pendingCapability)
    {
        var capability = this.catalog.Find(pendingCapability.CapabilityId);
        if (capability is not null)
        {
            return capability;
        }

        var referrer = pendingCapability.ReferrerId is Guid referrerId
            ? $" referenced by '{referrerId}'"
            : string.Empty;
        throw new KeyNotFoundException(
            $"Capability '{pendingCapability.CapabilityId}'{referrer} is not registered.");
    }

    private static Stack<PendingCapability> CreatePending(
        IReadOnlyList<Guid> capabilityIds,
        PrivacyClass privacy)
    {
        ArgumentNullException.ThrowIfNull(capabilityIds);
        CapabilityPrivacyPolicy.EnsureDefined(privacy);
        var pendingCapabilityIds = new Stack<PendingCapability>();
        for (var index = capabilityIds.Count - 1; index >= 0; index--)
        {
            pendingCapabilityIds.Push(new PendingCapability(capabilityIds[index], null));
        }

        return pendingCapabilityIds;
    }

    private static void PushRelated(
        CapabilityEntry capability,
        Stack<PendingCapability> pendingCapabilityIds)
    {
        for (var index = capability.RelatedCapabilities.Count - 1; index >= 0; index--)
        {
            pendingCapabilityIds.Push(
                new PendingCapability(
                    capability.RelatedCapabilities[index],
                    capability.CapabilityId));
        }
    }

    private readonly record struct PendingCapability(Guid CapabilityId, Guid? ReferrerId);
}
