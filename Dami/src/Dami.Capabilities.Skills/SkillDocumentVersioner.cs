using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Skills;

/// <summary>Computes the semantic filesystem version of an authored skill document.</summary>
public sealed class SkillDocumentVersioner
{
    /// <summary>Computes the exact version the bounded filesystem loader will publish.</summary>
    public string Compute(SkillDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        SkillDescriptor descriptor = CreateDescriptor(document);
        SkillDescriptorValidator.Validate(descriptor, 1024);
        using var hash = new SkillVersionHash();
        hash.AppendDescriptor(descriptor);
        hash.AppendText(document.Body);
        foreach (string reference in descriptor.References!)
        {
            hash.AppendText(document.References[reference]);
        }

        return hash.Complete();
    }

    internal static SkillDescriptor CreateDescriptor(SkillDocument document)
    {
        string[] references = document.References.Keys.ToArray();
        Array.Sort(references, StringComparer.Ordinal);
        return new SkillDescriptor
        {
            Id = document.SkillId,
            Name = document.Name,
            Description = document.Description,
            Tags = document.Tags.ToArray(),
            RelatedCapabilities = document.RelatedCapabilities.ToArray(),
            References = references,
        };
    }
}
