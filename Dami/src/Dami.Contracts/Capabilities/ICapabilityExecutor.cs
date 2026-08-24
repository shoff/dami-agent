namespace Dami.Contracts.Capabilities;

/// <summary>Executes source-neutral capability invocations.</summary>
public interface ICapabilityExecutor
{
    /// <summary>Executes one trace-aware request or throws when it cannot truthfully complete.</summary>
    Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken);
}
