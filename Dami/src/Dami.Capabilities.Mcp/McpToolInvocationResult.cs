namespace Dami.Capabilities.Mcp;

/// <summary>A protocol-neutral MCP tool result.</summary>
public sealed class McpToolInvocationResult
{
    /// <summary>Creates an MCP tool result.</summary>
    public McpToolInvocationResult(string output, bool isError)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.Output = output;
        this.IsError = isError;
    }

    /// <summary>Gets the model-visible remote output.</summary>
    public string Output { get; }

    /// <summary>Gets whether the server reported a tool-level error.</summary>
    public bool IsError { get; }
}
