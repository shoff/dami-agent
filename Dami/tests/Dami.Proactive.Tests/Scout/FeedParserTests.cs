using Dami.Proactive.Scout;
using Xunit;

namespace Dami.Proactive.Tests.Scout;

/// <summary>RSS 2.0 and Atom parsing, pure.</summary>
public sealed class FeedParserTests
{
    [Fact]
    public void Parse_Should_Read_Rss_Items()
    {
        const string xml = """
            <rss version="2.0"><channel>
              <item><title>pgvector internals</title><link>https://example.com/a</link>
                <pubDate>Fri, 21 Aug 2026 10:00:00 GMT</pubDate></item>
              <item><title>airbrush thinning ratios</title><link>https://example.com/b</link></item>
            </channel></rss>
            """;

        var items = FeedParser.Parse(xml);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Parse_Should_Read_The_Rss_Publication_Date()
    {
        const string xml = """
            <rss version="2.0"><channel>
              <item><title>t</title><link>https://example.com/a</link>
                <pubDate>Fri, 21 Aug 2026 10:00:00 GMT</pubDate></item>
            </channel></rss>
            """;

        var items = FeedParser.Parse(xml);

        Assert.Equal(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero), items[0].PublishedAt);
    }

    [Fact]
    public void Parse_Should_Read_Atom_Entries()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry><title>a talk on span apis</title>
                <link rel="alternate" href="https://example.com/talk"/>
                <published>2026-08-20T09:00:00Z</published></entry>
            </feed>
            """;

        var items = FeedParser.Parse(xml);

        Assert.Equal("https://example.com/talk", items[0].Link);
    }

    [Fact]
    public void Parse_Should_Skip_An_Item_With_No_Link()
    {
        const string xml = """
            <rss version="2.0"><channel>
              <item><title>orphan</title></item>
              <item><title>kept</title><link>https://example.com/kept</link></item>
            </channel></rss>
            """;

        var items = FeedParser.Parse(xml);

        Assert.Single(items);
    }

    [Fact]
    public void Parse_Should_Return_Empty_For_Garbage()
    {
        Assert.Empty(FeedParser.Parse("this is not xml at all <<<"));
    }
}
