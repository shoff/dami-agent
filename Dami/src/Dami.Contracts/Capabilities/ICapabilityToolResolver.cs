namespace Dami.Contracts.Capabilities;

/// <summary>Selects the bounded tool schemas relevant to one stated intent.</summary>
public interface ICapabilityToolResolver
{
    /// <summary>Returns only the tool schemas selected for this turn.</summary>
    Task<IReadOnlyList<CapabilityToolSchema>> ResolveAsync(
        string intent,
        CancellationToken cancellationToken);
}
