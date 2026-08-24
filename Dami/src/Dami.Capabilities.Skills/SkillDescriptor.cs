using System.Text.Json.Serialization;

namespace Dami.Capabilities.Skills;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class SkillDescriptor
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("relatedCapabilities")]
    public Guid[]? RelatedCapabilities { get; set; }

    [JsonPropertyName("references")]
    public string[]? References { get; set; }
}
