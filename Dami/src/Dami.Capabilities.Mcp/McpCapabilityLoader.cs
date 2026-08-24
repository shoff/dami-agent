using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Mcp;

/// <summary>Publishes safely normalized MCP tools to the source-neutral registries.</summary>
public sealed class McpCapabilityLoader
{
    private readonly McpCapabilityNormalizer normalizer;
    private readonly ICapabilityRegistrar registrar;
    private readonly ICapabilityToolSchemaRegistrar schemaRegistrar;

    /// <summary>Creates the MCP registration handoff.</summary>
    public McpCapabilityLoader(
        McpCapabilityNormalizer normalizer,
        ICapabilityRegistrar registrar,
        ICapabilityToolSchemaRegistrar schemaRegistrar)
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(schemaRegistrar);
        this.normalizer = normalizer;
        this.registrar = registrar;
        this.schemaRegistrar = schemaRegistrar;
    }

    /// <summary>Discovers, normalizes, validates, and publishes one server's tools.</summary>
    public async Task<IReadOnlyList<CapabilityEntry>> LoadAsync(
        McpServerRegistration server,
        IMcpToolSource source,
        DateTimeOffset registeredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(source);
        IReadOnlyList<McpToolDescriptor> tools = await source
            .DiscoverToolsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PreparedCapability> prepared = await this.PrepareAsync(
            server, source, tools, registeredAt, cancellationToken).ConfigureAwait(false);
        this.Publish(prepared);
        return Array.AsReadOnly(prepared.Select(item => item.Entry).ToArray());
    }

    private async Task<IReadOnlyList<PreparedCapability>> PrepareAsync(
        McpServerRegistration server,
        IMcpToolSource source,
        IReadOnlyList<McpToolDescriptor> tools,
        DateTimeOffset registeredAt,
        CancellationToken cancellationToken)
    {
        var prepared = new PreparedCapability[tools.Count];
        for (var index = 0; index < tools.Count; index++)
        {
            McpToolDescriptor tool = tools[index];
            CapabilityEntry entry = await this.normalizer
                .NormalizeAsync(server, tool, registeredAt, cancellationToken).ConfigureAwait(false);
            var parameters = source.FindSchema(tool.SchemaReference)
                ?? throw new InvalidDataException(
                    $"MCP tool '{tool.Name}' has no cached schema.");
            prepared[index] = new PreparedCapability(
                entry,
                new CapabilityToolSchema(
                    entry.CapabilityId, entry.Name, entry.Description, parameters));
        }

        return prepared;
    }

    private void Publish(IReadOnlyList<PreparedCapability> prepared)
    {
        foreach (PreparedCapability item in prepared)
        {
            this.registrar.Register(item.Entry);
            this.schemaRegistrar.Register(item.Schema);
        }
    }

    private sealed record PreparedCapability(
        CapabilityEntry Entry,
        CapabilityToolSchema Schema);
}
