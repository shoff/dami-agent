using Npgsql;

namespace Dami.Host;

/// <summary>Runtime activity over time, bucketed for a chart.</summary>
/// <remarks>
/// The execution event stream already records everything the runtime does; nothing showed
/// it as a shape. A rolling chart answers questions a list cannot — whether the tier is
/// working at all right now, whether egress spikes when the scout wakes, whether tool use
/// is climbing — and every number here is a count of durable events, not a sample.
///
/// Bucketing happens in Postgres with <c>date_bin</c> against one <c>now()</c>, because a
/// client bucketing on its own clock draws a chart that disagrees with the ledger it is
/// meant to be showing.
/// </remarks>
internal static class ActivityEndpoints
{
    private const int DEFAULT_MINUTES = 120;
    private const int MAX_MINUTES = 10080;
    private const int DEFAULT_BUCKETS = 60;
    private const int MAX_BUCKETS = 240;

    /// <summary>Event types collapsed into the series worth watching.</summary>
    private static readonly (string Series, string[] Types)[] series =
    [
        ("turns", ["TraceStarted"]),
        ("tools", ["ToolStarted"]),
        ("egress", ["EgressRequested"]),
        ("workers", ["WorkerStarted"]),
        ("produced", ["Surfaced", "Concluded", "Observed", "FactRecorded"]),
    ];

    /// <summary>Maps the activity read surface.</summary>
    internal static void MapDamiActivity(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/activity", ReadAsync);
    }

    private static async Task<IResult> ReadAsync(
        NpgsqlDataSource dataSource,
        int? minutes,
        int? buckets,
        CancellationToken cancellationToken)
    {
        var window = minutes ?? DEFAULT_MINUTES;
        var slots = buckets ?? DEFAULT_BUCKETS;
        if (window is <= 0 or > MAX_MINUTES || slots is <= 0 or > MAX_BUCKETS)
        {
            return Results.BadRequest(new
            {
                error = $"minutes must be 1..{MAX_MINUTES} and buckets 1..{MAX_BUCKETS}",
            });
        }

        var counts = await CountAsync(dataSource, window, slots, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(new
        {
            minutes = window,
            buckets = slots,
            secondsPerBucket = window * 60.0 / slots,
            series = series.Select(item => new
            {
                name = item.Series,
                values = Enumerable.Range(0, slots)
                    .Select(slot => counts.GetValueOrDefault((item.Series, slot))),
            }),
        });
    }

    /// <remarks>
    /// One query for every series. The bucket index is computed from the same
    /// <c>now()</c> the window is cut from, so the last slot is always "the interval
    /// ending now" rather than whenever the caller thinks now is.
    /// </remarks>
    private static async Task<Dictionary<(string Series, int Slot), int>> CountAsync(
        NpgsqlDataSource dataSource,
        int minutes,
        int buckets,
        CancellationToken cancellationToken)
    {
        var mapping = string.Join(" ", series.Select((item, index) =>
            $"when type = any(@t{index}) then '{item.Series}'"));

        await using var command = dataSource.CreateCommand(
            "with bounds as (select now() - make_interval(mins => @minutes) as from_at, now() as to_at) "
            + "select case " + mapping + " end as series, "
            + "  least(@buckets - 1, greatest(0, floor("
            + "    extract(epoch from (occurred_at - bounds.from_at))"
            + "    / (extract(epoch from (bounds.to_at - bounds.from_at)) / @buckets))::int)) as slot, "
            + "  count(*) "
            + "from dami.execution_events, bounds "
            + "where occurred_at >= bounds.from_at "
            + "group by 1, 2 having case " + mapping + " end is not null;");
        command.Parameters.AddWithValue("minutes", minutes);
        command.Parameters.AddWithValue("buckets", buckets);
        for (var index = 0; index < series.Length; index++)
        {
            command.Parameters.AddWithValue($"t{index}", series[index].Types);
        }

        var counts = new Dictionary<(string, int), int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            counts[(reader.GetString(0), reader.GetInt32(1))] = (int)reader.GetInt64(2);
        }

        return counts;
    }
}
