using Dami.Contracts.Capabilities;

namespace Dami.Capabilities;

/// <summary>Maps semantic capability selection to the exact advertised tool schemas.</summary>
public sealed class SemanticCapabilityToolResolver : ICapabilityToolResolver
{
    private readonly ICapabilityResolver capabilityResolver;
    private readonly ICapabilityToolSchemaCatalog schemaCatalog;

    /// <summary>Creates the semantic tool-schema resolver.</summary>
    public SemanticCapabilityToolResolver(
        ICapabilityResolver capabilityResolver,
        ICapabilityToolSchemaCatalog schemaCatalog)
    {
        ArgumentNullException.ThrowIfNull(capabilityResolver);
        ArgumentNullException.ThrowIfNull(schemaCatalog);
        this.capabilityResolver = capabilityResolver;
        this.schemaCatalog = schemaCatalog;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CapabilityToolSchema>> ResolveAsync(
        string intent,
        CancellationToken cancellationToken)
    {
        var bundle = await this.capabilityResolver.ResolveAsync(intent, cancellationToken)
            .ConfigureAwait(false);
        var schemas = new List<CapabilityToolSchema>(bundle.Capabilities.Count);
        foreach (var capability in bundle.Capabilities)
        {
            if (capability.Kind != CapabilityKind.Tool)
            {
                continue;
            }

            var schema = this.schemaCatalog.Find(capability.CapabilityId)
                ?? throw new InvalidDataException(
                    $"Selected tool '{capability.CapabilityId}' has no registered schema.");
            schemas.Add(schema);
        }

        return schemas.AsReadOnly();
    }
}
