using Dami.Capabilities;
using Dami.Capabilities.Mcp;

namespace Dami.Host;

/// <summary>Configured MCP servers owned by the interactive Host.</summary>
public sealed class McpHostOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "Mcp";

    /// <summary>Gets configured servers in deterministic startup order.</summary>
    public IList<McpServerHostOptions> Servers { get; } = [];
}

/// <summary>Configuration shape for one explicit MCP registration.</summary>
public sealed class McpServerHostOptions
{
    /// <summary>Gets or sets the stable server identifier.</summary>
    public Guid ServerId { get; set; }

    /// <summary>Gets or sets the local display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the absolute Streamable HTTP endpoint.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Gets or sets the wire transport.</summary>
    public McpTransportKind Transport { get; set; } = McpTransportKind.StreamableHttp;

    /// <summary>Gets or sets the explicitly assigned trust level.</summary>
    public TrustLevel Trust { get; set; } = TrustLevel.Untrusted;

    internal McpServerRegistration ToRegistration()
    {
        return new McpServerRegistration(
            this.ServerId,
            this.Name,
            this.Endpoint ?? throw new InvalidOperationException("An MCP endpoint is required."),
            this.Transport,
            this.Trust);
    }
}
