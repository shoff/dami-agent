using System.IO.Pipelines;
using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Dami.Capabilities.Mcp.Tests;

public sealed class McpServerConnectionTests
{
    [Fact]
    public async Task Operations_Should_Open_Explicit_Provenance_Scopes()
    {
        var (server, transport, cancellation, serverRun) = StartServer();
        await using var ownedServer = server;
        using var ownedCancellation = cancellation;
        var registration = CreateRegistration("scoped");
        var scopes = new RecordingScopeFactory();
        EgressOperationContext connect = CreateContext("connect MCP server");
        EgressOperationContext discovery = CreateContext("discover MCP tools");
        EgressOperationContext invocation = CreateContext("invoke MCP capability");
        await using var connection = await McpServerConnection.ConnectAsync(
            registration, transport, scopes, connect, CancellationToken.None);

        await connection.DiscoverToolsAsync(discovery, CancellationToken.None);
        using var arguments = JsonDocument.Parse("""{"city":"Austin"}""");
        await connection.InvokeAsync(
            "weather", arguments.RootElement, invocation, CancellationToken.None);
        await connection.DisposeAsync();

        Assert.Equal([connect, discovery, invocation, connect], scopes.Contexts);
        Assert.Equal(0, scopes.ActiveCount);
        await StopServerAsync(cancellation, serverRun);
    }

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
    public async Task ConnectRemoteAsync_Should_Require_The_Authorized_Gate_And_Scope_Initialization()
    {
        var registration = CreateRegistration("remote");
        using var handler = new StubAuthorizedHandler();
        var scopes = new RecordingScopeFactory();
        EgressOperationContext context = CreateContext("connect remote MCP server");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            McpServerConnection.ConnectRemoteAsync(
                registration, handler, scopes, context, cancellation.Token));

        Assert.Same(context, Assert.Single(scopes.Contexts));
        Assert.Equal(0, scopes.ActiveCount);
    }

    [Fact]
    public async Task DiscoverToolsAsync_Should_Cache_Schemas_Behind_Local_References()
    {
        var (server, transport, serverCancellation, serverRun) = StartServer();
        await using var ownedServer = server;
        using var ownedServerCancellation = serverCancellation;
        var registration = CreateRegistration("weather");
        await using var connection = await McpServerConnection.ConnectAsync(
            registration, transport, CancellationToken.None);

        var tools = await connection.DiscoverToolsAsync(CreateContext(), CancellationToken.None);

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
            "weather", arguments.RootElement, CreateContext(), CancellationToken.None);
        Assert.False(result.IsError);
        Assert.Equal("sunny in Austin", result.Output);
        await connection.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => connection.DiscoverToolsAsync(CreateContext(), CancellationToken.None));
        await StopServerAsync(serverCancellation, serverRun);
    }

    [Fact]
    public async Task Loaded_Tool_Should_Execute_Through_The_SourceNeutral_Dispatcher()
    {
        var (server, transport, serverCancellation, serverRun) = StartServer();
        await using var ownedServer = server;
        using var ownedServerCancellation = serverCancellation;
        var registration = CreateRegistration("weather");
        await using var connection = await McpServerConnection.ConnectAsync(
            registration, transport, CancellationToken.None);
        var capabilities = new CapabilityRegistry();
        var schemas = new CapabilityToolSchemaRegistry();
        var invocations = new McpCapabilityRegistry();
        var loader = new McpCapabilityLoader(
            new McpCapabilityNormalizer(new UnexpectedSummarizer()),
            capabilities, schemas, invocations);
        var loaded = await loader.LoadAsync(
            registration, connection, DateTimeOffset.UnixEpoch,
            CreateContext("discover MCP tools"), CancellationToken.None);
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
            () => connection.InvokeAsync(
                "wait", arguments.RootElement, CreateContext(), cancellation.Token));

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
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.Egressable, ExecutionOrigin.UserTurn,
            new CapabilityInvocation(capabilityId, arguments.RootElement));
    }

    private static McpServerRegistration CreateRegistration(string name)
    {
        return new McpServerRegistration(
            Guid.NewGuid(), name, new Uri("https://weather.example/mcp"),
            McpTransportKind.StreamableHttp, TrustLevel.Trusted);
    }

    private static EgressOperationContext CreateContext(string purpose = "test MCP operation")
    {
        return new EgressOperationContext(
            purpose, PrivacyClass.Egressable,
            Guid.NewGuid(), Guid.NewGuid(), ExecutionOrigin.UserTurn);
    }

    private sealed class RecordingScopeFactory : IEgressOperationScopeFactory
    {
        private int activeCount;

        public List<EgressOperationContext> Contexts { get; } = [];

        public int ActiveCount => Volatile.Read(ref this.activeCount);

        public IDisposable Begin(EgressOperationContext context)
        {
            this.Contexts.Add(context);
            Interlocked.Increment(ref this.activeCount);
            return new Scope(this);
        }

        private sealed class Scope(RecordingScopeFactory owner) : IDisposable
        {
            private RecordingScopeFactory? owner = owner;

            public void Dispose()
            {
                RecordingScopeFactory? active = Interlocked.Exchange(ref this.owner, null);
                if (active is not null)
                {
                    Interlocked.Decrement(ref active.activeCount);
                }
            }
        }
    }

    private sealed class StubAuthorizedHandler : HttpMessageHandler, IMcpEgressHttpHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A pre-cancelled connect must not reach the network.");
        }
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
