using Dami.Contracts.Context;

namespace Dami.Capabilities;

/// <summary>Resolves stated intent into a turn-ready capability bundle.</summary>
public interface ICapabilityResolver
{
    /// <summary>Retrieves, reranks, and expands capabilities relevant to an intent.</summary>
    Task<CapabilityBundle> ResolveAsync(
        string intent,
        PrivacyClass privacy,
        CancellationToken cancellationToken);
}
