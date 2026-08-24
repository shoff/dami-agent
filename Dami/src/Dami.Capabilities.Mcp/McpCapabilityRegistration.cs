namespace Dami.Capabilities.Mcp;

/// <summary>Binds one stable capability identifier to an owned MCP server connection.</summary>
public sealed class McpCapabilityRegistration
{
    /// <summary>Creates an executable MCP capability registration.</summary>
    public McpCapabilityRegistration(
        Guid capabilityId,
        Guid serverId,
        string toolName,
        IMcpToolInvoker invoker)
    {
        if (capabilityId == Guid.Empty)
        {
            throw new ArgumentException("An MCP capability id cannot be empty.", nameof(capabilityId));
        }

        if (serverId == Guid.Empty)
        {
            throw new ArgumentException("An MCP server id cannot be empty.", nameof(serverId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(invoker);
        this.CapabilityId = capabilityId;
        this.ServerId = serverId;
        this.ToolName = toolName;
        this.Invoker = invoker;
    }

    /// <summary>Gets the stable source-neutral capability identifier.</summary>
    public Guid CapabilityId { get; }

    /// <summary>Gets the locally configured server identifier.</summary>
    public Guid ServerId { get; }

    /// <summary>Gets the exact remote tool name.</summary>
    public string ToolName { get; }

    /// <summary>Gets the connection that owns remote invocation.</summary>
    public IMcpToolInvoker Invoker { get; }
}
