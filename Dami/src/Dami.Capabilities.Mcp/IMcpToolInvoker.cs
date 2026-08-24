using System.Text.Json;
using Dami.Contracts.Privacy;

namespace Dami.Capabilities.Mcp;

/// <summary>Invokes one named MCP tool without exposing SDK protocol types.</summary>
public interface IMcpToolInvoker
{
    /// <summary>Calls a remote tool with snapshotted source-neutral JSON arguments.</summary>
    Task<McpToolInvocationResult> InvokeAsync(
        string toolName,
        JsonElement arguments,
        EgressOperationContext context,
        CancellationToken cancellationToken);
}
