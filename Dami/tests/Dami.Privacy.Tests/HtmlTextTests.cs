using Xunit;

namespace Dami.Privacy.Tests;

public sealed class HtmlTextTests
{
    [Fact]
    public void Extract_Should_Drop_Markup_Scripts_And_Styles_And_Keep_The_Words()
    {
        const string html = """
            <html><head><title>Job: .NET contractor</title><style>p{color:red}</style></head>
            <body><script>alert(1)</script><h1>Senior .NET &amp; Postgres</h1><p>Remote, 3 months.</p>
            <!-- hidden --><div>Rate: $120/hr</div></body></html>
            """;

        Assert.Equal("Job: .NET contractor", HtmlText.Title(html));
        Assert.Equal("Senior .NET & Postgres\nRemote, 3 months.\nRate: $120/hr", HtmlText.Extract(html, 1000));
    }

    [Fact]
    public void Extract_Should_Cap_The_Text_And_Say_So()
    {
        var text = HtmlText.Extract("<p>" + new string('a', 500) + "</p>", 100);

        Assert.True(text.Length <= 102, $"{text.Length} chars");
        Assert.EndsWith("…", text, StringComparison.Ordinal);
    }
}
