namespace Dami.Contracts.Capabilities;

/// <summary>An execution source owning a subset of stable capability identifiers.</summary>
public interface ICapabilityExecutionSource : ICapabilityExecutor
{
    /// <summary>Determines whether this source owns the capability identifier.</summary>
    bool Owns(Guid capabilityId);
}
