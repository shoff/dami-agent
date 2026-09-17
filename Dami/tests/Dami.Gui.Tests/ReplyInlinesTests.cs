using Xunit;

namespace Dami.Gui.Tests;

public sealed class ReplyInlinesTests
{
    [Theory]
    [InlineData("Keep **unfinished")]
    [InlineData("\\**literal**")]
    [InlineData("`unfinished")]
    [InlineData("plain 雪と星")]
    public void Parse_Should_Preserve_Unformatted_Text(string text)
    {
        Assert.Equal([new ReplyInline(text)], ReplyInlines.Parse(text));
    }

    [Fact]
    public void Parse_Should_Distinguish_Emphasis_And_Literal_Inline_Code()
    {
        Assert.Equal(
            [new ReplyInline("Use "), new ReplyInline("bold", "bold"), new ReplyInline(", "),
             new ReplyInline("gentle", "italic"), new ReplyInline(" and "), new ReplyInline("a ** b", "code"),
             new ReplyInline(". Keep **unfinished")],
            ReplyInlines.Parse("Use **bold**, *gentle* and `a ** b`. Keep **unfinished"));
    }
}
