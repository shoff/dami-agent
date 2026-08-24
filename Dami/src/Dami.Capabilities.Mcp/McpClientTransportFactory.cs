using ModelContextProtocol.Client;

namespace Dami.Capabilities.Mcp;

internal static class McpClientTransportFactory
{
    public static IClientTransport Create(McpServerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (!registration.Endpoint.IsLoopback)
        {
            throw new InvalidOperationException(
                "Remote MCP requires an egress-authorized transport boundary.");
        }

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = registration.Endpoint,
            Name = registration.Name,
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            OwnsSession = true,
        });
    }
}
