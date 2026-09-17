using System.Text.RegularExpressions;
using System.Text.Json;
using Dami.Contracts.Research;

namespace Dami.Privacy;

/// <summary>Extracts followable, typed references without exposing markup to the model.</summary>
public static partial class ContentReferences
{
    /// <summary>Returns unique HTTP references in document order.</summary>
    public static IReadOnlyList<ResearchReference> Extract(string content, Uri baseAddress, string? mediaType)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var references = new List<ResearchReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (mediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            AddJsonIfValid(content, references, seen);
        }
        else
        {
            AddHtml(content, baseAddress, references, seen);
        }

        return references;
    }

    private static void AddJsonIfValid(
        string content,
        List<ResearchReference> references,
        HashSet<string> seen)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            AddJson(document.RootElement, string.Empty, references, seen);
        }
        catch (JsonException)
        {
            // Untrusted servers mislabel and truncate content. Preserve its bounded text,
            // but never infer followable addresses from a document that did not parse.
        }
    }

    private static void AddHtml(
        string html,
        Uri baseAddress,
        List<ResearchReference> references,
        HashSet<string> seen)
    {
        foreach (Match tag in ReferenceTag().Matches(html))
        {
            var attributes = Attributes(tag.Groups["attributes"].Value);
            if (!attributes.TryGetValue("href", out var href)
                || href.StartsWith('#')
                || !TryNormalize(baseAddress, href, out var address)
                || !seen.Add(address.AbsoluteUri))
            {
                continue;
            }

            var tagName = tag.Groups["tag"].Value;
            var relation = attributes.GetValueOrDefault("rel", string.Empty);
            var contentType = attributes.GetValueOrDefault("type", string.Empty);
            var label = tagName.Equals("a", StringComparison.OrdinalIgnoreCase)
                ? HtmlText.Extract(tag.Groups["body"].Value, 256)
                : attributes.GetValueOrDefault("title", relation);
            references.Add(new ResearchReference(
                address,
                string.IsNullOrWhiteSpace(label) ? address.AbsoluteUri : label,
                Kind(address, relation, contentType)));
        }
    }

    private static void AddJson(
        JsonElement element,
        string label,
        List<ResearchReference> references,
        HashSet<string> seen)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                AddJson(property.Value, property.Name, references, seen);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AddJson(item, label, references, seen);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(element.GetString(), UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
            || !seen.Add(address.AbsoluteUri))
        {
            return;
        }

        references.Add(new ResearchReference(address, label, JsonKind(address, label)));
    }

    private static ResearchReferenceKind JsonKind(Uri address, string label)
    {
        if (label.Contains("download", StringComparison.OrdinalIgnoreCase)
            || label.Contains("data", StringComparison.OrdinalIgnoreCase)
            || label.Contains("export", StringComparison.OrdinalIgnoreCase))
        {
            return ResearchReferenceKind.Data;
        }

        return label.Contains("api", StringComparison.OrdinalIgnoreCase)
            ? ResearchReferenceKind.Api
            : Kind(address, string.Empty, string.Empty);
    }

    private static IReadOnlyDictionary<string, string> Attributes(string text)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in Attribute().Matches(text))
        {
            var value = attribute.Groups["double"].Success
                ? attribute.Groups["double"].Value
                : attribute.Groups["single"].Success
                    ? attribute.Groups["single"].Value
                    : attribute.Groups["bare"].Value;
            attributes[attribute.Groups["name"].Value] = value;
        }

        return attributes;
    }

    private static ResearchReferenceKind Kind(Uri address, string relation, string contentType)
    {
        var path = address.AbsolutePath;
        if (relation.Contains("alternate", StringComparison.OrdinalIgnoreCase)
            && (contentType.Contains("rss", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("atom", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".rss", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".atom", StringComparison.OrdinalIgnoreCase)))
        {
            return ResearchReferenceKind.Feed;
        }

        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("csv", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ResearchReferenceKind.Data;
        }

        return address.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/", StringComparison.OrdinalIgnoreCase)
                ? ResearchReferenceKind.Api
                : ResearchReferenceKind.Page;
    }

    private static bool TryNormalize(Uri baseAddress, string href, out Uri address)
    {
        address = null!;
        if (!Uri.TryCreate(baseAddress, href.Trim(), out var candidate)
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var normalized = new UriBuilder(candidate) { Fragment = string.Empty }.Uri;
        address = normalized;
        return true;
    }

    [GeneratedRegex(
        @"<(?<tag>a)\b(?<attributes>[^>]*)>(?<body>.*?)</a\s*>|<(?<tag>link)\b(?<attributes>[^>]*)/?>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ReferenceTag();

    [GeneratedRegex(
        @"(?<name>[A-Za-z_:][A-Za-z0-9_:.-]*)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s>]+))",
        RegexOptions.IgnoreCase)]
    private static partial Regex Attribute();
}
