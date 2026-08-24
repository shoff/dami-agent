using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dami.Capabilities.Mcp;

/// <summary>Owns one MCP client connection and its discovered tool-schema cache.</summary>
public sealed class McpServerConnection : IMcpToolSource, IAsyncDisposable
{
    private readonly McpToolSchemaCache schemaCache;
    private McpClient? client;

    private McpServerConnection(McpServerRegistration registration, McpClient client)
    {
        this.schemaCache = new McpToolSchemaCache(registration.ServerId);
        this.client = client;
    }

    /// <summary>Connects to and owns one registered server transport.</summary>
    public static async Task<McpServerConnection> ConnectAsync(
        McpServerRegistration registration,
        CancellationToken cancellationToken)
    {
        IClientTransport transport = McpClientTransportFactory.Create(registration);
        return await ConnectAsync(registration, transport, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<McpServerConnection> ConnectAsync(
        McpServerRegistration registration,
        IClientTransport transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(transport);
        var client = await McpClient.CreateAsync(
            transport, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new McpServerConnection(registration, client);
    }

    /// <summary>Discovers remote tools while retaining their schemas outside model context.</summary>
    public async Task<IReadOnlyList<McpToolDescriptor>> DiscoverToolsAsync(
        CancellationToken cancellationToken)
    {
        McpClient activeClient = this.GetClient();
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
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("MCP tool arguments must be a JSON object.", nameof(arguments));
        }

        McpClient activeClient = this.GetClient();
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
            await owned.DisposeAsync().ConfigureAwait(false);
        }
    }

    private McpClient GetClient()
    {
        return Volatile.Read(ref this.client)
            ?? throw new ObjectDisposedException(nameof(McpServerConnection));
    }
}
