namespace Dami.Capabilities.Mcp;

/// <summary>Remote tool metadata with its schema isolated behind a local reference.</summary>
public sealed record McpToolDescriptor(
    string Name,
    string? Description,
    string SchemaReference,
    string Version);
