namespace Dami.Capabilities.Mcp;

/// <summary>One explicitly trusted MCP server endpoint.</summary>
public sealed record McpServerRegistration
{
    /// <summary>Creates a server registration.</summary>
    public McpServerRegistration(
        Guid serverId,
        string name,
        Uri endpoint,
        McpTransportKind transport,
        TrustLevel trust)
    {
        if (serverId == Guid.Empty)
        {
            throw new ArgumentException("An MCP server id cannot be empty.", nameof(serverId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureEndpoint(endpoint);
        if (!Enum.IsDefined(transport))
        {
            throw new ArgumentOutOfRangeException(nameof(transport));
        }

        if (!Enum.IsDefined(trust))
        {
            throw new ArgumentOutOfRangeException(nameof(trust));
        }

        this.ServerId = serverId;
        this.Name = name;
        this.Endpoint = endpoint;
        this.Transport = transport;
        this.Trust = trust;
    }

    /// <summary>Gets the stable local server identifier.</summary>
    public Guid ServerId { get; }

    /// <summary>Gets the local display name.</summary>
    public string Name { get; }

    /// <summary>Gets the configured server endpoint.</summary>
    public Uri Endpoint { get; }

    /// <summary>Gets the configured wire transport.</summary>
    public McpTransportKind Transport { get; }

    /// <summary>Gets the explicit trust assigned to server-provided content.</summary>
    public TrustLevel Trust { get; }

    private static void EnsureEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("An MCP endpoint must be absolute.", nameof(endpoint));
        }

        bool allowed = endpoint.Scheme == Uri.UriSchemeHttps
            || (endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback);
        if (!allowed)
        {
            throw new ArgumentException(
                "An MCP endpoint must use HTTPS unless it is loopback.", nameof(endpoint));
        }
    }
}
