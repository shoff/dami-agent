using Dami.Contracts.Context;

namespace Dami.Contracts.Capabilities;

/// <summary>Selects tools and deferred skills for one stated turn intent.</summary>
public interface ICapabilitySelectionResolver
{
    /// <summary>Returns one source-neutral selection from one semantic lookup.</summary>
    Task<CapabilitySelection> ResolveAsync(
        string intent,
        PrivacyClass privacy,
        CancellationToken cancellationToken);
}
