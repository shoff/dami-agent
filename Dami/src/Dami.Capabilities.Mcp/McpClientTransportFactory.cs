using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
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

        return new HttpClientTransport(CreateOptions(registration));
    }

    public static IClientTransport CreateRemote<THandler>(
        McpServerRegistration registration,
        THandler egressHandler)
        where THandler : HttpMessageHandler, IMcpEgressHttpHandler
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(egressHandler);
        if (registration.Endpoint.IsLoopback)
        {
            throw new InvalidOperationException(
                "The authorized remote MCP factory does not accept loopback endpoints.");
        }

        var client = new HttpClient(egressHandler, disposeHandler: false);
        return new HttpClientTransport(
            CreateOptions(registration), client, NullLoggerFactory.Instance, ownsHttpClient: true);
    }

    private static HttpClientTransportOptions CreateOptions(McpServerRegistration registration)
    {
        return new HttpClientTransportOptions
        {
            Endpoint = registration.Endpoint,
            Name = registration.Name,
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            OwnsSession = true,
        };
    }
}
