using System.Net;
using System.Text.RegularExpressions;

namespace Dami.Privacy;

/// <summary>Turns a page into the words on it. Pure, and deliberately crude.</summary>
/// <remarks>
/// Scripts, styles and tags go; entities are decoded; whitespace collapses. What is left
/// is what a reader would have read, which is all a research turn needs and all it
/// should get — markup is where the surprises live.
/// </remarks>
public static partial class HtmlText
{
    /// <summary>The page title, or empty.</summary>
    public static string Title(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var match = TitleTag().Match(html);
        return match.Success ? Collapse(WebUtility.HtmlDecode(match.Groups[1].Value)) : string.Empty;
    }

    /// <summary>The readable text, capped at <paramref name="maxChars"/>.</summary>
    public static string Extract(string html, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxChars, 0);
        var text = Invisible().Replace(html, " ");
        text = BlockEnd().Replace(text, "\n");
        text = Tag().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = Collapse(text);
        return text.Length <= maxChars ? text : text[..maxChars].TrimEnd() + " …";
    }

    private static string Collapse(string text)
    {
        var lines = text.Split('\n')
            .Select(line => Spaces().Replace(line, " ").Trim())
            .Where(line => line.Length > 0);
        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<(script|style|noscript|svg|head|title)[^>]*>.*?</\1>|<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Invisible();

    [GeneratedRegex(@"</?(p|div|br|li|tr|h[1-6]|section|article|header|footer|nav|blockquote|pre|table)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t\r\f\v ]+")]
    private static partial Regex Spaces();
}
