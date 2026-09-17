using Xunit;

namespace Dami.Gui.Tests;

public sealed class ReplyDocumentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\n\r\n  ")]
    public void Parse_Should_Not_Create_Empty_Prose_Blocks(string text)
    {
        Assert.Empty(ReplyDocument.Parse(text));
    }

    [Theory]
    [InlineData("<script>alert('hello')</script>")]
    [InlineData("``not a fence")]
    [InlineData("    ```indented")]
    [InlineData("####### not a heading")]
    public void Parse_Should_Leave_Unsupported_Or_Incomplete_Syntax_Visible(string text)
    {
        Assert.Equal([new ReplyBlock("text", text)], ReplyDocument.Parse(text));
    }

    [Fact]
    public void Parse_Should_Reject_Null_Text()
    {
        Assert.Throws<ArgumentNullException>(() => ReplyDocument.Parse(null!));
    }

    [Fact]
    public void Parse_Should_Preserve_Paragraphs_And_Separate_Fenced_Code()
    {
        var blocks = ReplyDocument.Parse("A useful example.\n\n```csharp\n  var x = 1;\n```\nKeep going.");

        Assert.Equal(
            [new ReplyBlock("text", "A useful example."), new ReplyBlock("code", "  var x = 1;\n", "csharp"), new ReplyBlock("text", "Keep going.")],
            blocks);
    }

    [Theory]
    [InlineData("```py\nprint('hello')", "print('hello')")]
    [InlineData("```py\r\n  print('hello')\r\n```", "  print('hello')\r\n")]
    [InlineData("```py\n\n```", "\n")]
    public void Parse_Should_Preserve_Exact_Code_During_Streaming(string text, string expected)
    {
        Assert.Equal([new ReplyBlock("code", expected, "py")], ReplyDocument.Parse(text));
    }

    [Theory]
    [InlineData("````md\n```cs\nx\n```\n````", "```cs\nx\n```\n", "md")]
    [InlineData("~~~sh\necho hi\n~~~", "echo hi\n", "sh")]
    [InlineData("```txt\n```not a closing fence\nend\n```", "```not a closing fence\nend\n", "txt")]
    public void Parse_Should_Respect_Fence_Character_Length_And_Closing_Syntax(string text, string code, string language)
    {
        Assert.Equal([new ReplyBlock("code", code, language)], ReplyDocument.Parse(text));
    }

    [Fact]
    public void Parse_Should_Give_Headings_Paragraphs_Lists_And_Quotes_Their_Own_Blocks()
    {
        var text = "## A direction\nFirst paragraph.\nStill here.\n\nSecond paragraph.\n- Small step\n1. Next step\n> Remember this";

        Assert.Equal(
            [new ReplyBlock("h2", "A direction"), new ReplyBlock("text", "First paragraph.\nStill here."),
             new ReplyBlock("text", "Second paragraph."), new ReplyBlock("list", "• Small step"),
             new ReplyBlock("list", "1. Next step"), new ReplyBlock("quote", "Remember this")],
            ReplyDocument.Parse(text));
    }
}
