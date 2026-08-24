using System.Text.Json;

namespace Dami.Capabilities.Mcp;

/// <summary>Exposes one MCP server's discovered tools and locally cached schemas.</summary>
public interface IMcpToolSource
{
    /// <summary>Discovers the current remote tool metadata.</summary>
    Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(
        CancellationToken cancellationToken);

    /// <summary>Finds a schema retained behind a discovery-issued local reference.</summary>
    JsonElement? FindSchema(string schemaReference);
}
