using ModelContextProtocol.Client;
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
}
