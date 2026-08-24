namespace Dami.Capabilities.Mcp;

/// <summary>Converts remote tool metadata into safe source-neutral registry entries.</summary>
public sealed class McpCapabilityNormalizer
{
    private readonly IMcpDescriptionSummarizer summarizer;

    /// <summary>Creates the normalizer.</summary>
    public McpCapabilityNormalizer(IMcpDescriptionSummarizer summarizer)
    {
        ArgumentNullException.ThrowIfNull(summarizer);
        this.summarizer = summarizer;
    }

    /// <summary>Normalizes one discovered tool without admitting untrusted prose.</summary>
    public async Task<CapabilityEntry> NormalizeAsync(
        McpServerRegistration server,
        McpToolDescriptor tool,
        DateTimeOffset registeredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tool);
        Guid capabilityId = McpCapabilityIdentity.Create(server.ServerId, tool.Name);
        string candidateDescription = await this.DescriptionAsync(
            server, tool, cancellationToken).ConfigureAwait(false);
        string description = server.Trust == TrustLevel.Untrusted
            ? McpDescriptionSummary.Validate(candidateDescription, tool.Description ?? string.Empty)
            : candidateDescription;
        return new CapabilityEntry(
            capabilityId,
            McpCapabilityIdentity.AdvertisedName(capabilityId),
            description,
            CapabilityKind.Tool,
            CapabilitySource.Mcp,
            server.Trust,
            ["mcp", server.Name],
            tool.SchemaReference,
            null,
            [],
            tool.Version,
            registeredAt);
    }

    private Task<string> DescriptionAsync(
        McpServerRegistration server,
        McpToolDescriptor tool,
        CancellationToken cancellationToken)
    {
        if (server.Trust == TrustLevel.Trusted)
        {
            return Task.FromResult(string.IsNullOrWhiteSpace(tool.Description)
                ? "Trusted MCP tool."
                : tool.Description);
        }

        return string.IsNullOrWhiteSpace(tool.Description)
            ? Task.FromResult("Remote MCP tool.")
            : this.summarizer.SummarizeAsync(
                server.Name, tool.Name, tool.Description, cancellationToken);
    }
}
