using System.Data;
using Dami.Contracts.Domains;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Domains;

/// <summary>
/// The conversation log as two daily series: turns per local day, and turns in the small
/// hours (midnight to five) — the "late night" the reflection pass could see but never
/// count.
/// </summary>
public sealed class PostgresConversationActivitySource : IDailySeriesSource
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;

    /// <summary>Creates the source.</summary>
    public PostgresConversationActivitySource(NpgsqlDataSource dataSource, IOptions<PostgresOptions> storeOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailySeries>> ReadAsync(
        DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);
        await using var command = this.CreateCommand(from, to, zone);
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

        var turns = new List<DailyPoint>();
        var late = new List<DailyPoint>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var day = await reader.GetFieldValueAsync<DateOnly>(0, cancellationToken).ConfigureAwait(false);
            turns.Add(new DailyPoint(day, await reader.GetFieldValueAsync<int>(1, cancellationToken).ConfigureAwait(false)));
            var smallHours = await reader.GetFieldValueAsync<int>(2, cancellationToken).ConfigureAwait(false);
            if (smallHours > 0)
            {
                late.Add(new DailyPoint(day, smallHours));
            }
        }

        return
        [
            new DailySeries("conversation-turns", "conversations with Dami", "turns", turns),
            new DailySeries("late-night-turns", "conversations between midnight and 5am", "turns", late),
        ];
    }

    private NpgsqlCommand CreateCommand(DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var command = this.dataSource.CreateCommand(
            $"""
            select local_day,
                   count(*)::int as turns,
                   count(*) filter (where local_hour < 5)::int as late
              from (select (requested_at at time zone @zone)::date as local_day,
                           extract(hour from requested_at at time zone @zone) as local_hour
                      from {this.storeOptions.SchemaName}.conversation_turns) t
             where local_day between @from and @to
             group by local_day
             order by local_day;
            """);
        command.Parameters.AddWithValue("zone", zone.Id);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        return command;
    }
}
