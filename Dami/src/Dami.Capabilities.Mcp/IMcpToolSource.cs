using System.Text.Json;
using Dami.Contracts.Privacy;

namespace Dami.Capabilities.Mcp;

/// <summary>Exposes one MCP server's discovered tools and locally cached schemas.</summary>
public interface IMcpToolSource : IMcpToolInvoker
{
    /// <summary>Discovers the current remote tool metadata.</summary>
    Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(
        EgressOperationContext context,
        CancellationToken cancellationToken);

    /// <summary>Finds a schema retained behind a discovery-issued local reference.</summary>
    JsonElement? FindSchema(string schemaReference);
}
