using System.Text.RegularExpressions;

namespace Dami.Gui;

/// <summary>Reads common emphasis and literal inline code; unsupported syntax stays visible.</summary>
public static partial class ReplyInlines
{
    /// <summary>Returns text spans without modifying the stored message.</summary>
    public static IReadOnlyList<ReplyInline> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spans = new List<ReplyInline>();
        var start = 0;
        foreach (Match match in Formatting().Matches(text))
        {
            if (match.Index > start)
            {
                spans.Add(new ReplyInline(text[start..match.Index]));
            }

            var value = match.Value;
            var width = value.StartsWith("**", StringComparison.Ordinal) ? 2 : 1;
            var kind = value[0] == '`' ? "code" : width == 2 ? "bold" : "italic";
            spans.Add(new ReplyInline(value[width..^width], kind));
            start = match.Index + match.Length;
        }

        if (start < text.Length)
        {
            spans.Add(new ReplyInline(text[start..]));
        }

        return spans;
    }

    [GeneratedRegex(@"(?<![\\*`])(`[^`\r\n]+`|\*\*[^*\r\n]+\*\*|\*[^*\r\n]+\*)(?![*`])")]
    private static partial Regex Formatting();
}
