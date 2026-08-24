using Dami.Contracts.Events;
using Npgsql;

namespace Dami.Host;

/// <summary>Trace replay, the live event feed, and the vital signs.</summary>
public static class EventEndpoints
{
    private const int PAGE = 200;

    /// <summary>Maps the event routes.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/traces/{id}", async (
            string id, IExecutionEventStore store, CancellationToken token) =>
        {
            var traceId = Guid.TryParse(id, out var parsed)
                ? parsed
                : await store.FindTraceByPrefixAsync(id, token).ConfigureAwait(false);
            return traceId is null
                ? Results.NotFound()
                : Results.Ok(Collect.Async(store.ReplayAsync(traceId.Value, token)));
        });

        // The GUI's live feed: poll with the last sequence seen.
        app.MapGet("/events", (long after, IExecutionEventStore store, CancellationToken token) =>
            Results.Ok(Collect.Async(store.ReadSinceAsync(after, PAGE, token))));

        app.MapGet("/stats", async (NpgsqlDataSource dataSource, CancellationToken token) =>
        {
            var sections = new Dictionary<string, List<string>>();
            foreach (var (title, sql) in StatsSections.all)
            {
                sections[title] = await SectionAsync(dataSource, sql, token).ConfigureAwait(false);
            }

            return Results.Ok(sections);
        });
    }

    private static async Task<List<string>> SectionAsync(
        NpgsqlDataSource dataSource,
        string sql,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }
}
