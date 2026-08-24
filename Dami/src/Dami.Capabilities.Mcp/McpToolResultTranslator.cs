using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Dami.Capabilities.Mcp;

/// <summary>Translates MCP wire results without silently dropping content blocks.</summary>
internal static class McpToolResultTranslator
{
    /// <summary>Produces model-visible text or complete protocol JSON for richer results.</summary>
    public static string Translate(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.StructuredContent is not null
            || !TryMeasurePlainText(result.Content, out var textLength))
        {
            return JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        }

        return JoinText(result.Content, textLength);
    }

    private static bool TryMeasurePlainText(IList<ContentBlock> content, out int length)
    {
        length = content.Count - 1;
        foreach (ContentBlock item in content)
        {
            if (item is not TextContentBlock text)
            {
                length = 0;
                return false;
            }

            length = checked(length + text.Text.Length);
        }

        return true;
    }

    private static string JoinText(IList<ContentBlock> content, int length)
    {
        if (content.Count == 0)
        {
            return string.Empty;
        }

        if (content.Count == 1)
        {
            return ((TextContentBlock)content[0]).Text;
        }

        return string.Create(length, content, static (destination, blocks) =>
        {
            var offset = 0;
            for (var index = 0; index < blocks.Count; index++)
            {
                if (index > 0)
                {
                    destination[offset++] = '\n';
                }

                string text = ((TextContentBlock)blocks[index]).Text;
                text.AsSpan().CopyTo(destination[offset..]);
                offset += text.Length;
            }
        });
    }
}
