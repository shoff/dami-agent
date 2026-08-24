using Xunit;

namespace Dami.Capabilities.Mcp.Tests;

public sealed class McpServerRegistrationTests
{
    [Fact]
    public void Constructor_Should_Preserve_Explicit_Server_Trust_And_Transport()
    {
        var serverId = Guid.NewGuid();
        var endpoint = new Uri("https://calendar.example/mcp");

        var registration = new McpServerRegistration(
            serverId,
            "calendar",
            endpoint,
            McpTransportKind.StreamableHttp,
            TrustLevel.Untrusted);

        Assert.Equal(serverId, registration.ServerId);
        Assert.Equal("calendar", registration.Name);
        Assert.Equal(endpoint, registration.Endpoint);
        Assert.Equal(McpTransportKind.StreamableHttp, registration.Transport);
        Assert.Equal(TrustLevel.Untrusted, registration.Trust);
    }

    [Fact]
    public void Constructor_Should_Reject_Missing_Or_Unsupported_Configuration()
    {
        Assert.Throws<ArgumentException>(() => Registration(serverId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Registration(name: "   "));
        Assert.Throws<ArgumentNullException>(() => new McpServerRegistration(
            Guid.NewGuid(), "calendar", null!,
            McpTransportKind.StreamableHttp, TrustLevel.Untrusted));
        Assert.Throws<ArgumentException>(
            () => Registration(endpoint: new Uri("/mcp", UriKind.Relative)));
        Assert.Throws<ArgumentException>(
            () => Registration(endpoint: new Uri("http://calendar.example/mcp")));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Registration(transport: (McpTransportKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Registration(trust: (TrustLevel)99));
    }

    private static McpServerRegistration Registration(
        Guid? serverId = null,
        string name = "calendar",
        Uri? endpoint = null,
        McpTransportKind transport = McpTransportKind.StreamableHttp,
        TrustLevel trust = TrustLevel.Untrusted)
    {
        return new McpServerRegistration(
            serverId ?? Guid.NewGuid(), name,
            endpoint ?? new Uri("https://calendar.example/mcp"), transport, trust);
    }
}
