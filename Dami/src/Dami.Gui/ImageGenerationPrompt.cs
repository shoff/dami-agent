namespace Dami.Gui;

/// <summary>Recognizes explicit requests that should return pixels instead of prose.</summary>
public static class ImageGenerationPrompt
{
    private static readonly string[] prefixes =
    [
        "/image ",
        "create an image of ",
        "generate an image of ",
        "draw an image of ",
    ];

    /// <summary>Returns the rendering prompt, or null for an ordinary chat message.</summary>
    public static string? Extract(string message)
    {
        foreach (var prefix in prefixes)
        {
            if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var prompt = message[prefix.Length..].Trim();
            return prompt.Length > 0 ? prompt : null;
        }

        return null;
    }

    /// <summary>Returns a scene when ordinary language asks Dami for a self-portrait.</summary>
    public static string? DamiScene(string message)
    {
        var normalized = message.Trim();
        var lower = normalized.ToLowerInvariant();
        if (lower.StartsWith("surprise me", StringComparison.Ordinal)
            && (lower.Contains("what you are doing", StringComparison.Ordinal)
                || lower.Contains("what you're doing", StringComparison.Ordinal)
                || HasImageWord(lower)))
        {
            return "Surprise me with a fresh candid scene showing what Dami is doing right now.";
        }

        return SceneAfter(lower, normalized, "picture of yourself ")
            ?? SceneAfter(lower, normalized, "photo of yourself ")
            ?? SceneAfter(lower, normalized, "image of yourself ")
            ?? SceneAfter(lower, normalized, "picture of you ")
            ?? SceneAfter(lower, normalized, "photo of you ")
            ?? SceneAfter(lower, normalized, "image of you ");
    }

    private static bool HasImageWord(string message) =>
        message.Contains("picture", StringComparison.Ordinal)
        || message.Contains("photo", StringComparison.Ordinal)
        || message.Contains("image", StringComparison.Ordinal);

    private static string? SceneAfter(string lower, string original, string marker)
    {
        var index = lower.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var scene = original[(index + marker.Length)..].Trim().TrimEnd('.', '?', '!');
        return scene.Length > 0 ? scene : "A candid portrait of Dami looking directly at Steve.";
    }
}
