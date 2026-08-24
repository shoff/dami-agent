using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Native;

/// <summary>Executes one in-process native capability implementation.</summary>
public interface INativeCapabilityHandler
{
    /// <summary>Executes a trace-aware request and returns evidence-backed output.</summary>
    Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken);
}
