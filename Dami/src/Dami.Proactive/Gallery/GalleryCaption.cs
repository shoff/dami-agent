using System.Text.Json;

namespace Dami.Proactive.Gallery;

/// <summary>What to ask the vision model, and how to read what it says back. Pure.</summary>
/// <remarks>
/// The model is asked for JSON; when it answers in prose anyway — small local models
/// do — the prose is the caption and the tags are empty, which is still worth indexing.
/// </remarks>
public static class GalleryCaption
{
    private const int MAX_TAGS = 12;

    /// <summary>The captioning prompt.</summary>
    public const string PROMPT =
        "Describe this photograph for a searchable index. Reply with JSON only: "
        + "{\"caption\": one or two sentences naming the setting, what the woman is doing, her "
        + "expression, wardrobe, lighting and time of day; \"tags\": up to twelve short lowercase "
        + "keywords such as kitchen, evening, red dress, smiling, outdoors, coffee}. No names.";

    /// <summary>Reads the model's reply into a caption and tags, leniently.</summary>
    public static (string Caption, IReadOnlyList<string> Tags) Parse(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var text = reply.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start && TryParse(text[start..(end + 1)], out var parsed))
        {
            return parsed;
        }

        var prose = text.Length == 0 ? "(the vision model returned nothing)" : text;
        return (prose, []);
    }

    private static bool TryParse(string json, out (string, IReadOnlyList<string>) parsed)
    {
        parsed = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var caption = root.TryGetProperty("caption", out var captionElement) ? captionElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(caption))
            {
                return false;
            }

            var tags = root.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array
                ? tagsElement.EnumerateArray()
                    .Select(tag => tag.GetString()?.Trim().ToLowerInvariant())
                    .Where(tag => !string.IsNullOrEmpty(tag))
                    .Distinct(StringComparer.Ordinal)
                    .Take(MAX_TAGS)
                    .ToList()!
                : [];
            parsed = (caption.Trim(), tags!);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>What gets embedded: the caption, the tags, and the prompt that made it.</summary>
    public static string Embeddable(string caption, IReadOnlyList<string> tags, string prompt)
    {
        ArgumentNullException.ThrowIfNull(caption);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(prompt);
        var parts = new List<string> { caption };
        if (tags.Count > 0)
        {
            parts.Add("Tags: " + string.Join(", ", tags));
        }

        if (prompt.Length > 0)
        {
            parts.Add("Prompt: " + prompt);
        }

        return string.Join('\n', parts);
    }
}
