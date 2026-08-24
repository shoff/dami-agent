namespace Dami.Contracts.Capabilities;

/// <summary>Executes source-neutral capability invocations.</summary>
public interface ICapabilityExecutor
{
    /// <summary>Executes one invocation or throws when it cannot truthfully complete.</summary>
    Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityInvocation invocation,
        CancellationToken cancellationToken);
}
