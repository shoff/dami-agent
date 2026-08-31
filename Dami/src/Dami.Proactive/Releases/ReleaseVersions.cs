using System.Globalization;
using System.Text.RegularExpressions;

namespace Dami.Proactive.Releases;

/// <summary>Finds and compares dotted version numbers. Pure.</summary>
public static partial class ReleaseVersions
{
    /// <summary>The first dotted version in the text, or null when there is none.</summary>
    public static string? Extract(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = VersionPattern().Match(text);
        return match.Success ? match.Value : null;
    }

    /// <summary>Whether the candidate is strictly newer than the baseline.</summary>
    public static bool IsNewer(string candidate, string baseline) =>
        Compare(candidate, baseline) > 0;

    /// <summary>Sign of candidate minus baseline, per segment.</summary>
    /// <remarks>
    /// Numeric per segment, not lexical: "595.9" beats "595.84" as text and loses as a
    /// version. A missing segment counts as zero, so 10.0.400.1 is newer than 10.0.400.
    /// </remarks>
    public static int Compare(string candidate, string baseline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseline);

        var mine = candidate.Split('.');
        var theirs = baseline.Split('.');
        for (var index = 0; index < Math.Max(mine.Length, theirs.Length); index++)
        {
            var left = Segment(mine, index);
            var right = Segment(theirs, index);
            if (left != right)
            {
                return left > right ? 1 : -1;
            }
        }

        return 0;
    }

    private static int Segment(string[] parts, int index) =>
        index < parts.Length
            && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;

    [GeneratedRegex(@"\d+(?:\.\d+)+")]
    private static partial Regex VersionPattern();
}
