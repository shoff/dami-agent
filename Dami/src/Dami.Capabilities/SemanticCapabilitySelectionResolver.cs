using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;

namespace Dami.Capabilities;

/// <summary>Maps one semantic bundle to its selected tool and deferred-skill contracts.</summary>
public sealed class SemanticCapabilitySelectionResolver : ICapabilitySelectionResolver
{
    private readonly ICapabilityResolver capabilityResolver;
    private readonly ICapabilityToolSchemaCatalog schemaCatalog;

    /// <summary>Creates the source-neutral turn selection resolver.</summary>
    public SemanticCapabilitySelectionResolver(
        ICapabilityResolver capabilityResolver,
        ICapabilityToolSchemaCatalog schemaCatalog)
    {
        ArgumentNullException.ThrowIfNull(capabilityResolver);
        ArgumentNullException.ThrowIfNull(schemaCatalog);
        this.capabilityResolver = capabilityResolver;
        this.schemaCatalog = schemaCatalog;
    }

    /// <inheritdoc />
    public async Task<CapabilitySelection> ResolveAsync(
        string intent,
        PrivacyClass privacy,
        CancellationToken cancellationToken)
    {
        CapabilityBundle bundle = await this.capabilityResolver
            .ResolveAsync(intent, privacy, cancellationToken).ConfigureAwait(false);
        var tools = new List<CapabilityToolSchema>(bundle.Capabilities.Count);
        var skills = new List<SkillSelection>(bundle.Capabilities.Count);
        for (var index = 0; index < bundle.Capabilities.Count; index++)
        {
            CapabilityEntry capability = bundle.Capabilities[index];
            if (capability.Kind == CapabilityKind.Tool)
            {
                tools.Add(this.schemaCatalog.Find(capability.CapabilityId)
                    ?? throw new InvalidDataException(
                        $"Selected tool '{capability.CapabilityId}' has no registered schema."));
                continue;
            }

            skills.Add(new SkillSelection(
                capability.CapabilityId,
                capability.Name,
                capability.BodyReference!,
                capability.Version));
        }

        return new CapabilitySelection(tools, skills);
    }
}
