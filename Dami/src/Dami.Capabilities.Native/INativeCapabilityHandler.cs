using System.Text.Json;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Native;

/// <summary>Executes one in-process native capability implementation.</summary>
public interface INativeCapabilityHandler
{
    /// <summary>Executes validated JSON arguments and returns evidence-backed output.</summary>
    Task<CapabilityExecutionResult> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken);
}
