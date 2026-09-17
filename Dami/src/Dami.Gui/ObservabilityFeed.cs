using System.Globalization;
using System.Text.Json;

namespace Dami.Gui;

/// <summary>One headline count from the newest durable execution events.</summary>
public sealed record ObservabilityTile(string Label, string Value, string Detail, bool IsAlert = false);

/// <summary>One durable runtime event as the Observability tab presents it.</summary>
public sealed record ObservabilityEvent(
    long Sequence,
    string Time,
    string Trace,
    string TraceIdentity,
    string Origin,
    string Actor,
    string Type,
    string Status,
    string Label,
    bool IsAlert,
    bool IsRunning);

/// <summary>The bounded event window and its honest counts.</summary>
public sealed record ObservabilitySnapshot(
    IReadOnlyList<ObservabilityTile> Tiles,
    IReadOnlyList<ObservabilityEvent> Events);

/// <summary>Shapes canonical execution events into a live operational view.</summary>
public static class ObservabilityFeed
{
    /// <summary>Builds newest-first rows and counts over exactly the supplied window.</summary>
    public static ObservabilitySnapshot Build(JsonElement events)
    {
        if (events.ValueKind != JsonValueKind.Array)
        {
            return new ObservabilitySnapshot([], []);
        }

        var rows = events.EnumerateArray().Select(Row).OrderByDescending(item => item.Sequence).ToList();
        var running = rows.GroupBy(item => item.TraceIdentity)
            .Count(trace => trace.First().IsRunning);
        IReadOnlyList<ObservabilityTile> tiles =
        [
            new("EVENTS", Invariant(rows.Count), "newest durable rows"),
            new("RUNNING", Invariant(running), "operations active now"),
            new("EGRESS", Invariant(rows.Count(item => item.Type == "EgressRequested")), "outbound attempts"),
            new("ALERTS", Invariant(rows.Count(item => item.IsAlert)), "failed, refused, or non-2xx",
                rows.Any(item => item.IsAlert)),
        ];
        return new ObservabilitySnapshot(tiles, rows);
    }

    private static ObservabilityEvent Row(JsonElement item)
    {
        var traceId = item.GetProperty("traceId").GetGuid();
        var status = item.GetProperty("status").GetString() ?? string.Empty;
        var type = item.GetProperty("type").GetString() ?? string.Empty;
        var label = item.GetProperty("label").GetString() ?? string.Empty;
        var at = item.GetProperty("occurredAt").GetDateTimeOffset();
        var alert = PassWaterfall.IsAlert(new PassMoment(at, type, label, status));
        return new ObservabilityEvent(
            item.GetProperty("sequence").GetInt64(),
            at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            traceId.ToString("N")[..8],
            traceId.ToString("N"),
            item.GetProperty("origin").GetString() ?? string.Empty,
            item.GetProperty("actorId").GetString() ?? string.Empty,
            type,
            status,
            label,
            alert,
            status == "Running");
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
