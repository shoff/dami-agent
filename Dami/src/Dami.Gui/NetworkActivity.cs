using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Dami.Gui;

/// <summary>One fact from the latest sweep, flagged when it describes a fault.</summary>
public sealed record NetworkFactRow(string Category, string Description, bool IsProblem);

/// <summary>Something that differs between the last two sweeps.</summary>
public sealed record NetworkChange(string Kind, string Description);

/// <summary>One headline number for the Network tab.</summary>
public sealed record NetworkTile(string Label, string Value, string Detail);

/// <summary>Shapes /domains/network into the Network tab. Pure, JSON in, like TodayDigest.</summary>
/// <remarks>
/// The facts are the network collector's own sentences, one sweep per day, newest first.
/// Everything deterministic happens here; the one non-deterministic panel on the tab —
/// the analysis — is built from <see cref="AnalysisPrompt"/> and sent through a normal
/// local turn, so it is traced, LocalOnly, and labeled as the model's speculation.
/// </remarks>
public static class NetworkActivity
{
    /// <summary>Whether a collector sentence describes a fault.</summary>
    public static bool IsProblem(string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        return description.Contains("is down", StringComparison.Ordinal)
            || description.Contains("not listening", StringComparison.Ordinal)
            || description.Contains("does not answer", StringComparison.Ordinal)
            || description.Contains("unreachable", StringComparison.Ordinal);
    }

    /// <summary>The newest sweep's rows, faults first, then by category and text.</summary>
    public static List<NetworkFactRow> Latest(JsonElement facts)
    {
        var latest = LatestSweep(facts);
        return Rows(facts)
            .Where(row => row.AsOf == latest)
            .Select(row => new NetworkFactRow(row.Category, row.Description, IsProblem(row.Description)))
            .OrderByDescending(row => row.IsProblem)
            .ThenBy(row => row.Category, StringComparer.Ordinal)
            .ThenBy(row => row.Description, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>What the newest sweep says that the one before did not, and vice versa.</summary>
    public static List<NetworkChange> Changes(JsonElement facts)
    {
        var sweeps = Rows(facts).Select(row => row.AsOf).Distinct().OrderDescending().ToList();
        if (sweeps.Count < 2)
        {
            return [];
        }

        var latest = Descriptions(facts, sweeps[0]);
        var previous = Descriptions(facts, sweeps[1]);
        return latest.Except(previous).Select(text => new NetworkChange("appeared", text))
            .Concat(previous.Except(latest).Select(text => new NetworkChange("gone", text)))
            .ToList();
    }

    /// <summary>The headline numbers: sweep age, devices, services, faults.</summary>
    public static List<NetworkTile> Tiles(JsonElement facts)
    {
        var sweeps = Rows(facts).Select(row => row.AsOf).Distinct().OrderDescending().ToList();
        if (sweeps.Count == 0)
        {
            return [];
        }

        var latest = Rows(facts).Where(row => row.AsOf == sweeps[0]).ToList();
        var previousProblems = sweeps.Count > 1
            ? Rows(facts).Count(row => row.AsOf == sweeps[1] && IsProblem(row.Description))
            : 0;
        return
        [
            new NetworkTile("SWEEP", sweeps[0],
                Invariant($"{sweeps.Count} sweep(s) on record")),
            new NetworkTile("DEVICES", Invariant($"{latest.Count(row => row.Category == "device")}"),
                "answering on the LAN"),
            new NetworkTile("SERVICES", Invariant($"{latest.Count(row => row.Category == "service")}"),
                "listening on this host"),
            new NetworkTile("PROBLEMS", Invariant($"{latest.Count(row => IsProblem(row.Description))}"),
                Invariant($"{previousProblems} the sweep before")),
        ];
    }

    /// <summary>Fault counts per sweep, oldest first, for the trend chart.</summary>
    public static List<(DateTimeOffset At, double Value)> ProblemsBySweep(JsonElement facts)
    {
        return Rows(facts)
            .GroupBy(row => row.AsOf)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (
                new DateTimeOffset(
                    DateOnly.ParseExact(group.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    TimeOnly.MinValue, TimeSpan.Zero),
                (double)group.Count(row => IsProblem(row.Description))))
            .ToList();
    }

    /// <summary>The analysis request sent through a normal local turn.</summary>
    /// <remarks>
    /// The facts ride along verbatim so the model analyses what the tab is actually
    /// showing, and the instruction demands that guesses be labeled as speculation —
    /// the panel renders model output, and it must read as exactly that.
    /// </remarks>
    public static string AnalysisPrompt(JsonElement facts)
    {
        var report = new StringBuilder();
        foreach (var row in Rows(facts))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{row.AsOf} [{row.Category}] {row.Description}");
        }

        return $"""
            Steve is looking at the Network tab of Dami's desktop client. Below are the
            network collector's recorded facts, newest sweep first, one sweep per day.

            {report}
            In a few short lines: what stands out, what changed between sweeps, and what
            looks off or worth checking. You may speculate about causes — a new device's
            likely identity, why an interface is down — but label every guess as
            speculation. If everything is ordinary, say so in one line.
            """;
    }

    private static string? LatestSweep(JsonElement facts)
    {
        string? latest = null;
        foreach (var row in Rows(facts))
        {
            if (latest is null || string.CompareOrdinal(row.AsOf, latest) > 0)
            {
                latest = row.AsOf;
            }
        }

        return latest;
    }

    private static HashSet<string> Descriptions(JsonElement facts, string sweep) =>
        Rows(facts)
            .Where(row => row.AsOf == sweep)
            .Select(row => row.Description)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<(string AsOf, string Category, string Description)> Rows(
        JsonElement facts)
    {
        foreach (var fact in facts.EnumerateArray())
        {
            yield return (
                fact.GetProperty("asOf").GetString() ?? string.Empty,
                fact.GetProperty("category").GetString() ?? string.Empty,
                fact.GetProperty("description").GetString() ?? string.Empty);
        }
    }

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);
}
