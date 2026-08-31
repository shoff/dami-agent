using System.Text.Json;

namespace Dami.Proactive.Security;

/// <summary>One NuGet advisory from the GitHub advisory database.</summary>
public sealed record NugetAdvisory(
    string GhsaId,
    string Summary,
    string Severity,
    string Url,
    DateTimeOffset? PublishedAt,
    string Package,
    string Range);

/// <summary>Reads the two advisory wire formats. Pure, like the flows it sits beside.</summary>
public static class SecurityAdvisories
{
    /// <summary>Words every notice title carries; matching on them would alert on everything.</summary>
    private static readonly HashSet<string> boilerplate = new(StringComparer.Ordinal)
    {
        "vulnerability", "vulnerabilities", "security", "notice", "notices", "update",
        "updates", "several", "multiple", "issues", "regression", "linux", "kernel",
    };

    /// <summary>The NuGet vulnerabilities in a GitHub /advisories response.</summary>
    public static IReadOnlyList<NugetAdvisory> ParseGithub(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var advisories = new List<NugetAdvisory>();
            foreach (var advisory in document.RootElement.EnumerateArray())
            {
                Read(advisory, advisories);
            }

            return advisories;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The installed package a notice title names, or null.</summary>
    /// <remarks>
    /// A word heuristic, stated as such: "OpenSSL vulnerabilities" matches an installed
    /// <c>openssl</c>, "PostgreSQL" matches <c>postgresql-16</c>. Kernel notices are
    /// deliberately in the boilerplate set — kernel updates flow through the update
    /// manager, and alerting on every one would teach the reader to ignore the rest.
    /// </remarks>
    public static string? UsnMentions(string title, IReadOnlyList<string> installedPackages)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(installedPackages);

        foreach (var raw in title.Split(' ', ':', ',', '(', ')'))
        {
            var token = raw.Trim().ToLowerInvariant();
            if (token.Length < 4 || boilerplate.Contains(token) || AllDigitsOrDashes(token))
            {
                continue;
            }

            foreach (var package in installedPackages)
            {
                var name = package.ToLowerInvariant();
                if (name == token
                    || name.StartsWith(token + "-", StringComparison.Ordinal)
                    || name.StartsWith(token + ".", StringComparison.Ordinal))
                {
                    return package;
                }
            }
        }

        return null;
    }

    private static void Read(JsonElement advisory, List<NugetAdvisory> advisories)
    {
        if (!advisory.TryGetProperty("vulnerabilities", out var vulnerabilities)
            || vulnerabilities.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var vulnerability in vulnerabilities.EnumerateArray())
        {
            var package = vulnerability.GetProperty("package");
            if (!string.Equals(
                    package.GetProperty("ecosystem").GetString(), "nuget",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            advisories.Add(new NugetAdvisory(
                Text(advisory, "ghsa_id"),
                Text(advisory, "summary"),
                Text(advisory, "severity"),
                Text(advisory, "html_url"),
                DateTimeOffset.TryParse(Text(advisory, "published_at"), out var published)
                    ? published
                    : null,
                package.GetProperty("name").GetString() ?? string.Empty,
                Text(vulnerability, "vulnerable_version_range")));
        }
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool AllDigitsOrDashes(string token)
    {
        foreach (var character in token)
        {
            if (char.IsAsciiLetter(character))
            {
                return false;
            }
        }

        return true;
    }
}
