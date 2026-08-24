using ModelContextProtocol.Client;
using Dami.Contracts.Privacy;
using Xunit;

namespace Dami.Capabilities.Mcp.Tests;

public sealed class McpClientTransportFactoryTests
{
    [Fact]
    public void Create_Should_Map_The_Registration_To_Streamable_Http()
    {
        var registration = new McpServerRegistration(
            Guid.NewGuid(), "calendar", new Uri("http://127.0.0.1:5811/mcp"),
            McpTransportKind.StreamableHttp, TrustLevel.Untrusted);

        var transport = McpClientTransportFactory.Create(registration);

        var http = Assert.IsType<HttpClientTransport>(transport);
        Assert.Equal("calendar", http.Name);
    }

    [Fact]
    public void Create_Should_Reject_Remote_Endpoints_Without_An_Egress_Authorized_Transport()
    {
        var registration = new McpServerRegistration(
            Guid.NewGuid(), "remote", new Uri("https://mcp.example/mcp"),
            McpTransportKind.StreamableHttp, TrustLevel.Trusted);

        var exception = Assert.Throws<InvalidOperationException>(
            () => McpClientTransportFactory.Create(registration));

        Assert.Contains("egress", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateRemote_Should_Construct_The_Sdk_Transport_Only_From_The_Authorized_Gate()
    {
        var registration = new McpServerRegistration(
            Guid.NewGuid(), "remote", new Uri("https://mcp.example/mcp"),
            McpTransportKind.StreamableHttp, TrustLevel.Trusted);
        using var handler = new StubAuthorizedHandler();

        var transport = McpClientTransportFactory.CreateRemote(registration, handler);

        var http = Assert.IsType<HttpClientTransport>(transport);
        Assert.Equal("remote", http.Name);
    }

    private sealed class StubAuthorizedHandler : HttpMessageHandler, IMcpEgressHttpHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Transport construction must not send a request.");
        }
    }
}
