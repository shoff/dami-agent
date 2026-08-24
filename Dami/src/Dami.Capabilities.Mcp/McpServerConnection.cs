using System.Text.Json;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dami.Capabilities.Mcp;

/// <summary>Owns one MCP client connection and its discovered tool-schema cache.</summary>
public sealed class McpServerConnection : IMcpToolSource, IAsyncDisposable
{
    private readonly McpToolSchemaCache schemaCache;
    private readonly IEgressOperationScopeFactory scopeFactory;
    private readonly EgressOperationContext shutdownContext;
    private McpClient? client;

    private McpServerConnection(
        McpServerRegistration registration,
        McpClient client,
        IEgressOperationScopeFactory scopeFactory,
        EgressOperationContext shutdownContext)
    {
        this.schemaCache = new McpToolSchemaCache(registration.ServerId);
        this.client = client;
        this.scopeFactory = scopeFactory;
        this.shutdownContext = shutdownContext;
    }

    /// <summary>Connects to and owns one registered server transport.</summary>
    public static async Task<McpServerConnection> ConnectAsync(
        McpServerRegistration registration,
        CancellationToken cancellationToken)
    {
        IClientTransport transport = McpClientTransportFactory.Create(registration);
        return await ConnectAsync(
            registration, transport, LocalScopeFactory.Instance,
            LocalScopeFactory.CreateContext(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Connects a remote server through the dedicated authorized HTTP gate.</summary>
    public static Task<McpServerConnection> ConnectRemoteAsync<THandler>(
        McpServerRegistration registration,
        THandler egressHandler,
        IEgressOperationScopeFactory scopeFactory,
        EgressOperationContext connectContext,
        CancellationToken cancellationToken)
        where THandler : HttpMessageHandler, IMcpEgressHttpHandler
    {
        IClientTransport transport = McpClientTransportFactory.CreateRemote(
            registration, egressHandler);
        return ConnectAsync(
            registration, transport, scopeFactory, connectContext, cancellationToken);
    }

    internal static async Task<McpServerConnection> ConnectAsync(
        McpServerRegistration registration,
        IClientTransport transport,
        IEgressOperationScopeFactory scopeFactory,
        EgressOperationContext connectContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(connectContext);
        using IDisposable scope = scopeFactory.Begin(connectContext);
        var client = await McpClient.CreateAsync(
            transport, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new McpServerConnection(registration, client, scopeFactory, connectContext);
    }

    internal static Task<McpServerConnection> ConnectAsync(
        McpServerRegistration registration,
        IClientTransport transport,
        CancellationToken cancellationToken)
    {
        return ConnectAsync(
            registration, transport, LocalScopeFactory.Instance,
            LocalScopeFactory.CreateContext(), cancellationToken);
    }

    /// <summary>Discovers remote tools while retaining their schemas outside model context.</summary>
    public async Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(
        EgressOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        McpClient activeClient = this.GetClient();
        using IDisposable scope = this.scopeFactory.Begin(context);
        IList<McpClientTool> tools = await activeClient
            .ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return this.schemaCache.Replace(tools);
    }

    /// <summary>Finds a previously discovered schema by its local reference.</summary>
    public JsonElement? FindSchema(string schemaReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaReference);
        return this.schemaCache.Find(schemaReference);
    }

    /// <inheritdoc />
    public async Task<McpToolInvocationResult> InvokeAsync(
        string toolName,
        JsonElement arguments,
        EgressOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(context);
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("MCP tool arguments must be a JSON object.", nameof(arguments));
        }

        McpClient activeClient = this.GetClient();
        using IDisposable scope = this.scopeFactory.Begin(context);
        var mappedArguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (JsonProperty property in arguments.EnumerateObject())
        {
            mappedArguments.Add(property.Name, property.Value);
        }

        CallToolResult result = await activeClient.CallToolAsync(
            toolName, mappedArguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        string output = McpToolResultTranslator.Translate(result);
        return new McpToolInvocationResult(output, result.IsError == true);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        McpClient? owned = Interlocked.Exchange(ref this.client, null);
        if (owned is not null)
        {
            using IDisposable scope = this.scopeFactory.Begin(this.shutdownContext);
            await owned.DisposeAsync().ConfigureAwait(false);
        }
    }

    private McpClient GetClient()
    {
        return Volatile.Read(ref this.client)
            ?? throw new ObjectDisposedException(nameof(McpServerConnection));
    }

    private sealed class LocalScopeFactory : IEgressOperationScopeFactory
    {
        public static LocalScopeFactory Instance { get; } = new();

        public IDisposable Begin(EgressOperationContext context) => NoOpScope.Instance;

        public static EgressOperationContext CreateContext()
        {
            return new EgressOperationContext(
                "local MCP connection",
                PrivacyClass.LocalOnly,
                Guid.NewGuid(),
                Guid.NewGuid(),
                ExecutionOrigin.UserTurn);
        }

        private sealed class NoOpScope : IDisposable
        {
            public static NoOpScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
