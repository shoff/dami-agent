namespace Dami.Capabilities.Mcp;

/// <summary>A tool-level error reported by an MCP server.</summary>
public sealed class McpToolExecutionException : Exception
{
    /// <summary>Creates a source-neutral MCP tool error.</summary>
    public McpToolExecutionException(Guid capabilityId, string remoteMessage)
        : base($"MCP capability '{capabilityId}' reported an execution error.")
    {
        if (capabilityId == Guid.Empty)
        {
            throw new ArgumentException("An MCP capability id cannot be empty.", nameof(capabilityId));
        }

        ArgumentNullException.ThrowIfNull(remoteMessage);
        this.CapabilityId = capabilityId;
        this.RemoteMessage = remoteMessage;
    }

    /// <summary>Gets the stable capability identifier.</summary>
    public Guid CapabilityId { get; }

    /// <summary>Gets the remote diagnostic for controlled handling by the caller.</summary>
    public string RemoteMessage { get; }
}
