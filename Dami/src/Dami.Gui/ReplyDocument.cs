using System.Text;
using System.Text.RegularExpressions;

namespace Dami.Gui;

/// <summary>Separates plain reply text and fenced code without interpreting HTML.</summary>
public static partial class ReplyDocument
{
    /// <summary>Reads display blocks. The original message remains the source for full-message copying.</summary>
    public static IReadOnlyList<ReplyBlock> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var blocks = new List<ReplyBlock>();
        var buffer = new StringBuilder();
        Fence? fence = null;
        foreach (var line in Lines(text))
        {
            ReadLine(line, blocks, buffer, ref fence);
        }

        Flush(blocks, buffer, fence?.Language);
        return blocks;
    }

    private static void ReadLine(string line, List<ReplyBlock> blocks, StringBuilder buffer, ref Fence? fence)
    {
        var candidate = ReadFence(line);
        if (fence is null && candidate is not null)
        {
            Flush(blocks, buffer, null);
            fence = candidate;
        }
        else if (fence is not null && candidate is not null && candidate.Language.Length == 0
            && candidate.Marker[0] == fence.Marker[0] && candidate.Marker.Length >= fence.Marker.Length)
        {
            Flush(blocks, buffer, fence.Language);
            fence = null;
        }
        else
        {
            buffer.Append(line);
        }
    }

    private static Fence? ReadFence(string line)
    {
        var trimmed = line.TrimStart(' ');
        if (line.Length - trimmed.Length > 3 || trimmed.Length < 3 || trimmed[0] is not ('`' or '~'))
        {
            return null;
        }

        var length = 1;
        while (length < trimmed.Length && trimmed[length] == trimmed[0])
        {
            length++;
        }

        var language = trimmed[length..].Trim();
        return length < 3 || (trimmed[0] == '`' && language.Contains('`'))
            ? null : new Fence(trimmed[..length], language);
    }

    private static IEnumerable<string> Lines(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            end = end < 0 ? text.Length : end + 1;
            yield return text[start..end];
            start = end;
        }
    }

    private static void Flush(List<ReplyBlock> blocks, StringBuilder buffer, string? language)
    {
        if (buffer.Length > 0 || language is not null)
        {
            var text = buffer.ToString();
            if (language is null)
            {
                ReadProse(blocks, text);
            }
            else
            {
                blocks.Add(new ReplyBlock("code", text, language));
            }

            buffer.Clear();
        }
    }

    private static void ReadProse(List<ReplyBlock> blocks, string text)
    {
        var paragraph = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var match = Structure().Match(line);
            if (string.IsNullOrWhiteSpace(line) || match.Success)
            {
                FlushParagraph(blocks, paragraph);
                if (match.Success)
                {
                    blocks.Add(StructuredBlock(match));
                }
            }
            else
            {
                paragraph.Append(line).Append('\n');
            }
        }

        FlushParagraph(blocks, paragraph);
    }

    private static ReplyBlock StructuredBlock(Match match)
    {
        var marker = match.Groups[1].Value;
        var text = match.Groups[2].Value;
        return marker[0] switch
        {
            '#' => new ReplyBlock($"h{marker.Length}", text),
            '>' => new ReplyBlock("quote", text),
            '-' or '+' or '*' => new ReplyBlock("list", "• " + text),
            _ => new ReplyBlock("list", marker + " " + text),
        };
    }

    private static void FlushParagraph(List<ReplyBlock> blocks, StringBuilder paragraph)
    {
        if (paragraph.Length > 0)
        {
            blocks.Add(new ReplyBlock("text", paragraph.ToString().TrimEnd()));
            paragraph.Clear();
        }
    }

    [GeneratedRegex(@"^ {0,3}(#{1,6}|>|[-+*]|\d{1,9}[.)])\s+(.+)$")]
    private static partial Regex Structure();

    private sealed record Fence(string Marker, string Language);
}
