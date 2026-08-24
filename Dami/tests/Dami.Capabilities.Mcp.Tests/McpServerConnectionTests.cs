using System.IO.Pipelines;
using System.Text.Json;
using Dami.Contracts.Capabilities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Dami.Capabilities.Mcp.Tests;

public sealed class McpServerConnectionTests
{
    [Fact]
    public async Task ConnectAsync_Should_Honor_Cancellation_Before_Opening_The_Endpoint()
    {
        var registration = new McpServerRegistration(
            Guid.NewGuid(), "offline", new Uri("http://127.0.0.1:1/mcp"),
            McpTransportKind.StreamableHttp, TrustLevel.Untrusted);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => McpServerConnection.ConnectAsync(registration, cancellation.Token));
    }

    [Fact]
    public async Task DiscoverToolsAsync_Should_Cache_Schemas_Behind_Local_References()
    {
        var (server, transport, serverCancellation, serverRun) = StartServer();
        await using var ownedServer = server;
        using var ownedServerCancellation = serverCancellation;
        var registration = new McpServerRegistration(
            Guid.NewGuid(), "weather", new Uri("https://weather.example/mcp"),
            McpTransportKind.StreamableHttp, TrustLevel.Trusted);
        await using var connection = await McpServerConnection.ConnectAsync(
            registration, transport, CancellationToken.None);

        var tools = await connection.DiscoverToolsAsync(CancellationToken.None);

        var tool = Assert.Single(tools);
        Assert.Equal("weather", tool.Name);
        Assert.Equal("Look up the current weather.", tool.Description);
        Assert.Contains(registration.ServerId.ToString("D"), tool.SchemaReference);
        Assert.Equal(64, tool.Version.Length);
        Assert.All(tool.Version, character => Assert.True(char.IsAsciiHexDigitLower(character)));
        var schema = connection.FindSchema(tool.SchemaReference);
        Assert.NotNull(schema);
        Assert.Equal("object", schema.Value.GetProperty("type").GetString());
        using var arguments = JsonDocument.Parse("""{"city":"Austin"}""");
        McpToolInvocationResult result = await connection.InvokeAsync(
            "weather", arguments.RootElement, CancellationToken.None);
        Assert.False(result.IsError);
        Assert.Equal("sunny in Austin", result.Output);
        await connection.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => connection.DiscoverToolsAsync(CancellationToken.None));
        await StopServerAsync(serverCancellation, serverRun);
    }

    [Fact]
    public async Task Loaded_Tool_Should_Execute_Through_The_SourceNeutral_Dispatcher()
    {
        var (server, transport, serverCancellation, serverRun) = StartServer();
        await using var ownedServer = server;
        using var ownedServerCancellation = serverCancellation;
        var registration = new McpServerRegistration(
            Guid.NewGuid(), "weather", new Uri("https://weather.example/mcp"),
            McpTransportKind.StreamableHttp, TrustLevel.Trusted);
        await using var connection = await McpServerConnection.ConnectAsync(
            registration, transport, CancellationToken.None);
        var capabilities = new CapabilityRegistry();
        var schemas = new CapabilityToolSchemaRegistry();
        var invocations = new McpCapabilityRegistry();
        var loader = new McpCapabilityLoader(
            new McpCapabilityNormalizer(new UnexpectedSummarizer()),
            capabilities, schemas, invocations);
        var loaded = await loader.LoadAsync(
            registration, connection, DateTimeOffset.UnixEpoch, CancellationToken.None);
        var dispatcher = new CapabilityExecutorDispatcher(
            [new McpCapabilityExecutor(invocations, new McpCapabilityExecutorOptions())]);

        CapabilityExecutionResult result = await dispatcher.ExecuteAsync(
            CreateRequest(Assert.Single(loaded).CapabilityId), CancellationToken.None);

        Assert.Equal("sunny in Austin", result.Output);
        Assert.Equal("mcp", result.Evidence["source"]);
        await StopServerAsync(serverCancellation, serverRun);
    }

    [Fact]
    public async Task InvokeAsync_Should_Cancel_An_InFlight_Protocol_Call()
    {
        var tool = McpServerTool.Create(
            WaitUntilCancelledAsync,
            new McpServerToolCreateOptions { Name = "wait" });
        var (server, transport, serverCancellation, serverRun) = StartServer(tool);
        await using var ownedServer = server;
        using var ownedServerCancellation = serverCancellation;
        var registration = new McpServerRegistration(
            Guid.NewGuid(), "waiting", new Uri("https://wait.example/mcp"),
            McpTransportKind.StreamableHttp, TrustLevel.Trusted);
        await using var connection = await McpServerConnection.ConnectAsync(
            registration, transport, CancellationToken.None);
        using var arguments = JsonDocument.Parse("{}");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connection.InvokeAsync("wait", arguments.RootElement, cancellation.Token));

        await StopServerAsync(serverCancellation, serverRun);
    }

    private static (
        McpServer Server,
        StreamClientTransport Transport,
        CancellationTokenSource Cancellation,
        Task Run) StartServer()
    {
        return StartServer(McpServerTool.Create(
            (string city) => $"sunny in {city}",
            new McpServerToolCreateOptions
            {
                Name = "weather",
                Description = "Look up the current weather.",
            }));
    }

    private static (
        McpServer Server,
        StreamClientTransport Transport,
        CancellationTokenSource Cancellation,
        Task Run) StartServer(McpServerTool tool)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var server = McpServer.Create(
            new StreamServerTransport(
                clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
            new McpServerOptions
            {
                ToolCollection = [tool],
            });
        var transport = new StreamClientTransport(
            clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream());
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return (server, transport, cancellation, server.RunAsync(cancellation.Token));
    }

    private static async Task<string> WaitUntilCancelledAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        return "unreachable";
    }

    private static async Task StopServerAsync(
        CancellationTokenSource cancellation,
        Task serverRun)
    {
        await cancellation.CancelAsync();
        try
        {
            await serverRun.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static CapabilityExecutionRequest CreateRequest(Guid capabilityId)
    {
        using var arguments = JsonDocument.Parse("""{"city":"Austin"}""");
        return new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(),
            new CapabilityInvocation(capabilityId, arguments.RootElement));
    }

    private sealed class UnexpectedSummarizer : IMcpDescriptionSummarizer
    {
        public Task<string> SummarizeAsync(
            string serverName,
            string toolName,
            string untrustedDescription,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Trusted metadata must not be summarized.");
        }
    }
}
