using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Materializes and failure-atomically publishes approved sandboxed tools.</summary>
public sealed class SandboxedToolActivator : ISandboxedToolActivator
{
    private readonly ICapabilityCatalog capabilities;
    private readonly TimeProvider clock;
    private readonly ISandboxedCapabilityCatalog handlers;
    private readonly ISandboxedToolMaterializer materializer;
    private readonly SandboxedCapabilityPublisher publisher;
    private readonly ICapabilityToolSchemaCatalog schemas;

    /// <summary>Creates the exact runtime activation service.</summary>
    public SandboxedToolActivator(
        ISandboxedToolMaterializer materializer,
        SandboxedCapabilityPublisher publisher,
        ISandboxedCapabilityCatalog handlers,
        ICapabilityToolSchemaCatalog schemas,
        ICapabilityCatalog capabilities,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(materializer);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(clock);
        this.materializer = materializer;
        this.publisher = publisher;
        this.handlers = handlers;
        this.schemas = schemas;
        this.capabilities = capabilities;
        this.clock = clock;
    }

    /// <inheritdoc />
    public async Task ActivateAsync(
        ToolActivationRecoveryItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        SandboxedCapabilityRegistration registration = await this.materializer
            .MaterializeAsync(
                item.PromotionId, item.Proposal, item.Verification, cancellationToken)
            .ConfigureAwait(false);
        CapabilityToolSchema schema = item.Proposal.Request.Artifact.Schema;
        CapabilityEntry entry = CreateEntry(item, this.clock.GetUtcNow());
        Guid capabilityId = registration.CapabilityId;
        SandboxedCapabilityRegistration? existingHandler = this.handlers.Find(capabilityId);
        CapabilityToolSchema? existingSchema = this.schemas.Find(capabilityId);
        CapabilityEntry? existingEntry = this.capabilities.Find(capabilityId);
        if (existingHandler is null && existingSchema is null && existingEntry is null)
        {
            this.publisher.Publish(registration, schema, entry);
            return;
        }

        EnsureExact(existingHandler, existingSchema, existingEntry, registration, schema, entry);
    }

    private static CapabilityEntry CreateEntry(
        ToolActivationRecoveryItem item,
        DateTimeOffset registeredAt)
    {
        ToolProposalArtifact artifact = item.Proposal.Request.Artifact;
        Guid capabilityId = artifact.Schema.CapabilityId;
        return new CapabilityEntry(
            capabilityId, artifact.Schema.Name, artifact.Schema.Description,
            CapabilityKind.Tool, CapabilitySource.Sandboxed, TrustLevel.Trusted,
            artifact.Tags, $"sandboxed://{capabilityId:D}/schema", null, [],
            item.Proposal.ArtifactVersion, registeredAt);
    }

    private static void EnsureExact(
        SandboxedCapabilityRegistration? handler,
        CapabilityToolSchema? schema,
        CapabilityEntry? entry,
        SandboxedCapabilityRegistration expectedHandler,
        CapabilityToolSchema expectedSchema,
        CapabilityEntry expectedEntry)
    {
        bool exact = HandlerMatches(handler, expectedHandler)
            && SchemaMatches(schema, expectedSchema)
            && EntryMatches(entry, expectedEntry);
        if (!exact)
        {
            throw new InvalidDataException(
                "The live registries contain a partial or different sandboxed activation.");
        }
    }

    private static bool EntryMatches(CapabilityEntry? entry, CapabilityEntry expected)
    {
        return entry is not null
            && entry.CapabilityId == expected.CapabilityId
            && string.Equals(entry.Name, expected.Name, StringComparison.Ordinal)
            && string.Equals(entry.Description, expected.Description, StringComparison.Ordinal)
            && entry.Kind == expected.Kind
            && entry.Source == expected.Source
            && entry.Trust == expected.Trust
            && entry.Tags.SequenceEqual(expected.Tags, StringComparer.Ordinal)
            && string.Equals(entry.SchemaReference, expected.SchemaReference, StringComparison.Ordinal)
            && entry.BodyReference is null
            && entry.RelatedCapabilities.Count == 0
            && string.Equals(entry.Version, expected.Version, StringComparison.Ordinal);
    }

    private static bool HandlerMatches(
        SandboxedCapabilityRegistration? handler,
        SandboxedCapabilityRegistration expected)
    {
        return handler is not null
            && handler.CapabilityId == expected.CapabilityId
            && handler.Verification == expected.Verification
            && string.Equals(
                handler.ArtifactDirectory, expected.ArtifactDirectory, StringComparison.Ordinal);
    }

    private static bool SchemaMatches(
        CapabilityToolSchema? schema,
        CapabilityToolSchema expected)
    {
        return schema is not null
            && schema.CapabilityId == expected.CapabilityId
            && string.Equals(schema.Name, expected.Name, StringComparison.Ordinal)
            && string.Equals(schema.Description, expected.Description, StringComparison.Ordinal)
            && JsonElement.DeepEquals(schema.Parameters, expected.Parameters);
    }
}
