using System.Xml.Linq;

namespace Dami.Proactive.Scout;

/// <summary>Parses RSS 2.0 and Atom into feed items. Pure, so it tests without a network.</summary>
public static class FeedParser
{
    private static readonly XNamespace atom = "http://www.w3.org/2005/Atom";

    /// <summary>Parses a feed document. Unknown formats yield an empty list, not an error.</summary>
    public static IReadOnlyList<FeedItem> Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var root = document.Root;
        if (root is null)
        {
            return [];
        }

        return root.Name.LocalName == "feed" ? ParseAtom(root) : ParseRss(root);
    }

    private static IReadOnlyList<FeedItem> ParseRss(XElement root)
    {
        var items = new List<FeedItem>();

        foreach (var item in root.Descendants("item"))
        {
            var title = item.Element("title")?.Value?.Trim();
            var link = item.Element("link")?.Value?.Trim();

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link))
            {
                continue;
            }

            items.Add(new FeedItem(title, link, ParseDate(item.Element("pubDate")?.Value)));
        }

        return items;
    }

    private static IReadOnlyList<FeedItem> ParseAtom(XElement root)
    {
        var items = new List<FeedItem>();

        foreach (var entry in root.Elements(atom + "entry"))
        {
            var title = entry.Element(atom + "title")?.Value?.Trim();
            var link = entry.Elements(atom + "link")
                .FirstOrDefault(element => (string?)element.Attribute("rel") is null or "alternate")
                ?.Attribute("href")?.Value;

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link))
            {
                continue;
            }

            items.Add(new FeedItem(title, link, ParseDate(entry.Element(atom + "published")?.Value
                ?? entry.Element(atom + "updated")?.Value)));
        }

        return items;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
