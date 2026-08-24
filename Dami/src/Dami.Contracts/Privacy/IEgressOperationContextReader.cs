namespace Dami.Contracts.Privacy;

/// <summary>Reads the async-flow-local provenance for body-capable egress.</summary>
public interface IEgressOperationContextReader
{
    /// <summary>Gets the current operation, or null outside an explicit scope.</summary>
    EgressOperationContext? Current { get; }
}
