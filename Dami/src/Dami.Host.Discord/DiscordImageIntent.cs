using System.Text.RegularExpressions;

namespace Dami.Host.Discord;

/// <summary>What kind of picture, if any, a message is asking for. Pure.</summary>
/// <remarks>
/// Four literal prefixes were the whole grammar until 2026-09-04, when "Let's see an
/// image of you painting your toes" fell through to the frontier, which talked about
/// generating and attached nothing. A request for a picture of Dami goes to the
/// identity-preserving portrait route; any other request for a picture goes to the plain
/// generator; a message about a picture Steve sent is neither.
/// </remarks>
public static partial class DiscordImageIntent
{
    private const string DEFAULT_SCENE = "A candid portrait of Dami looking directly at Steve.";

    private static readonly string[] explicitPrefixes =
    [
        "/image ",
        "create an image of ",
        "generate an image of ",
        "draw an image of ",
    ];

    /// <summary>The picture a message asks for, or null when it asks for none.</summary>
    public static DiscordImageRequest? Classify(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();

        foreach (var prefix in explicitPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var prompt = trimmed[prefix.Length..].Trim();
                return prompt.Length > 0 ? new DiscordImageRequest(false, prompt) : null;
            }
        }

        if (AboutASentPicture().IsMatch(trimmed))
        {
            return null;
        }

        return Portrait(trimmed) ?? Picture(trimmed);
    }

    private static DiscordImageRequest? Portrait(string text)
    {
        var self = PortraitOfDami().Match(text);
        if (self.Success)
        {
            return new DiscordImageRequest(true, Scene(text[(self.Index + self.Length)..]));
        }

        var selfie = Selfie().Match(text);
        return selfie.Success
            ? new DiscordImageRequest(true, Scene("taking a selfie " + text[(selfie.Index + selfie.Length)..].Trim()))
            : null;
    }

    private static DiscordImageRequest? Picture(string text)
    {
        if (!ImageNoun().IsMatch(text) || !RequestCue().IsMatch(text))
        {
            return null;
        }

        var subject = PictureOf().Match(text);
        var prompt = subject.Success ? text[(subject.Index + subject.Length)..].Trim() : text;
        prompt = prompt.TrimEnd('.', '?', '!').Trim();
        return prompt.Length > 0 ? new DiscordImageRequest(false, prompt) : null;
    }

    /// <summary>"you painting your toes" becomes "Dami painting her toes".</summary>
    private static string Scene(string remainder)
    {
        var scene = remainder.Trim().TrimEnd('.', '?', '!').TrimStart(',', ' ').Trim();
        if (scene.Length == 0)
        {
            return DEFAULT_SCENE;
        }

        scene = Yourself().Replace(scene, "herself");
        scene = Your().Replace(scene, "her");
        scene = You().Replace(scene, "she");
        return "Dami " + scene;
    }

    [GeneratedRegex(@"\b(?:image|picture|photo|pic|portrait|snapshot|drawing|shot)s?\s+of\s+(?:you|yourself|u|dami)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PortraitOfDami();

    [GeneratedRegex(@"\bselfie\b", RegexOptions.IgnoreCase)]
    private static partial Regex Selfie();

    [GeneratedRegex(@"\b(?:image|picture|photo|pic|portrait|snapshot|drawing)s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImageNoun();

    [GeneratedRegex(@"\b(?:show|see|send|make|create|generate|draw|paint|take|want|give|get|post|share|let'?s|can you|could you|would you|please|pls|need)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RequestCue();

    [GeneratedRegex(@"\b(?:image|picture|photo|pic|portrait|snapshot|drawing)s?\s+of\s+", RegexOptions.IgnoreCase)]
    private static partial Regex PictureOf();

    [GeneratedRegex(@"\b(?:i sent|you sent|i attached|attached|this (?:image|picture|photo|pic)|that (?:image|picture|photo|pic)|the (?:image|picture|photo|pic) i )\b", RegexOptions.IgnoreCase)]
    private static partial Regex AboutASentPicture();

    [GeneratedRegex(@"\byourself\b", RegexOptions.IgnoreCase)]
    private static partial Regex Yourself();

    [GeneratedRegex(@"\byour\b", RegexOptions.IgnoreCase)]
    private static partial Regex Your();

    [GeneratedRegex(@"\byou\b", RegexOptions.IgnoreCase)]
    private static partial Regex You();
}

/// <summary>A picture to make: of Dami herself, or of whatever the text describes.</summary>
public sealed record DiscordImageRequest(bool OfDami, string Text);
