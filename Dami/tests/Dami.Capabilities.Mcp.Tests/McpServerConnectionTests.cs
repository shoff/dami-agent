using System.IO.Pipelines;
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
        var schema = connection.FindSchema(tool.SchemaReference);
        Assert.NotNull(schema);
        Assert.Equal("object", schema.Value.GetProperty("type").GetString());
        await connection.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => connection.DiscoverToolsAsync(CancellationToken.None));
        await StopServerAsync(serverCancellation, serverRun);
    }

    private static (
        McpServer Server,
        StreamClientTransport Transport,
        CancellationTokenSource Cancellation,
        Task Run) StartServer()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var server = McpServer.Create(
            new StreamServerTransport(
                clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
            new McpServerOptions
            {
                ToolCollection =
                [
                    McpServerTool.Create(
                        (string city) => $"sunny in {city}",
                        new McpServerToolCreateOptions
                        {
                            Name = "weather",
                            Description = "Look up the current weather.",
                        }),
                ],
            });
        var transport = new StreamClientTransport(
            clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream());
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return (server, transport, cancellation, server.RunAsync(cancellation.Token));
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
}
