namespace Dami.Capabilities.Skills;

internal static class SkillDescriptorValidator
{
    private const int MAX_DESCRIPTION_CHARACTERS = 500;
    private const int MAX_NAME_CHARACTERS = 100;
    private const int MAX_TAGS = 32;
    private const int MAX_TAG_CHARACTERS = 64;
    private const int MAX_REFERENCE_CHARACTERS = 240;

    public static void Validate(SkillDescriptor descriptor, int maxReferences)
    {
        if (descriptor.Id == Guid.Empty)
        {
            throw new InvalidDataException("Skill descriptor requires a non-empty id.");
        }

        EnsureSingleLine(descriptor.Name, MAX_NAME_CHARACTERS, "name");
        EnsureSingleLine(descriptor.Description, MAX_DESCRIPTION_CHARACTERS, "description");
        descriptor.Tags ??= [];
        descriptor.RelatedCapabilities ??= [];
        descriptor.References ??= [];
        ValidateTags(descriptor.Tags);
        ValidateRelatedCapabilities(descriptor.RelatedCapabilities);
        ValidateReferences(descriptor.References, maxReferences);
    }

    private static void ValidateTags(IReadOnlyList<string> tags)
    {
        if (tags.Count > MAX_TAGS)
        {
            throw new InvalidDataException($"Skill descriptor exceeds its bound of {MAX_TAGS} tags.");
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tags.Count; index++)
        {
            EnsureSingleLine(tags[index], MAX_TAG_CHARACTERS, "tag");
            if (!unique.Add(tags[index]))
            {
                throw new InvalidDataException($"Skill descriptor repeats tag '{tags[index]}'.");
            }
        }
    }

    private static void ValidateRelatedCapabilities(IReadOnlyList<Guid> relatedCapabilities)
    {
        var unique = new HashSet<Guid>();
        for (var index = 0; index < relatedCapabilities.Count; index++)
        {
            Guid capabilityId = relatedCapabilities[index];
            if (capabilityId == Guid.Empty || !unique.Add(capabilityId))
            {
                throw new InvalidDataException(
                    "Related capability identifiers must be non-empty and unique.");
            }
        }
    }

    private static void ValidateReferences(IReadOnlyList<string> references, int maxReferences)
    {
        if (references.Count > maxReferences)
        {
            throw new InvalidDataException(
                $"Skill descriptor exceeds its bound of {maxReferences} references.");
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < references.Count; index++)
        {
            EnsureSingleLine(references[index], MAX_REFERENCE_CHARACTERS, "reference");
            if (!unique.Add(references[index]))
            {
                throw new InvalidDataException(
                    $"Skill descriptor repeats reference '{references[index]}'.");
            }
        }
    }

    private static void EnsureSingleLine(string? value, int maxCharacters, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maxCharacters
            || value.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new InvalidDataException(
                $"Skill {field} must be one nonblank line of at most {maxCharacters} characters.");
        }
    }
}
