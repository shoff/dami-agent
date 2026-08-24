using ModelContextProtocol.Protocol;
using Xunit;

namespace Dami.Capabilities.Mcp.Tests;

public sealed class McpToolResultTranslatorTests
{
    [Fact]
    public void Translate_Should_Preserve_NonText_Content_As_Protocol_Json()
    {
        var result = new CallToolResult
        {
            Content = [ImageContentBlock.FromBytes(new byte[] { 1, 2, 3 }, "image/png")],
        };

        string output = McpToolResultTranslator.Translate(result);

        Assert.Contains("\"type\":\"image\"", output, StringComparison.Ordinal);
        Assert.Contains("AQID", output, StringComparison.Ordinal);
    }
}
