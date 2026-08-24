using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;

namespace Dami.Capabilities;

/// <summary>Maps semantic capability selection to the exact advertised tool schemas.</summary>
public sealed class SemanticCapabilityToolResolver : ICapabilityToolResolver
{
    private readonly SemanticCapabilitySelectionResolver selectionResolver;

    /// <summary>Creates the semantic tool-schema resolver.</summary>
    public SemanticCapabilityToolResolver(
        ICapabilityResolver capabilityResolver,
        ICapabilityToolSchemaCatalog schemaCatalog)
    {
        ArgumentNullException.ThrowIfNull(capabilityResolver);
        ArgumentNullException.ThrowIfNull(schemaCatalog);
        this.selectionResolver = new SemanticCapabilitySelectionResolver(
            capabilityResolver, schemaCatalog);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CapabilityToolSchema>> ResolveAsync(
        string intent,
        PrivacyClass privacy,
        CancellationToken cancellationToken)
    {
        CapabilitySelection selection = await this.selectionResolver
            .ResolveAsync(intent, privacy, cancellationToken)
            .ConfigureAwait(false);
        return selection.Tools;
    }
}
