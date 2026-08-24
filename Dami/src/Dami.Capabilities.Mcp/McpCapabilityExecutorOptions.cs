namespace Dami.Capabilities.Mcp;

/// <summary>Bounds model-visible MCP execution results.</summary>
public sealed class McpCapabilityExecutorOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "McpCapabilityExecutor";

    /// <summary>Gets or sets the maximum translated output length.</summary>
    public int MaxOutputCharacters { get; set; } = 65_536;
}
