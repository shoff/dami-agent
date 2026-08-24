namespace Dami.Capabilities.Mcp;

internal static class McpDescriptionSummary
{
    private const int MAX_LENGTH = 240;

    public static string Validate(string answer, string sourceDescription)
    {
        string summary = answer.Trim();
        string source = sourceDescription.Trim();
        bool unsafeOutput = summary.Length is 0 or > MAX_LENGTH
            || summary.Contains('\r', StringComparison.Ordinal)
            || summary.Contains('\n', StringComparison.Ordinal)
            || (source.Length > 0 && summary.Contains(source, StringComparison.Ordinal));
        if (unsafeOutput)
        {
            throw new InvalidDataException(
                "The local model did not produce a bounded neutral MCP description.");
        }

        return summary;
    }
}
