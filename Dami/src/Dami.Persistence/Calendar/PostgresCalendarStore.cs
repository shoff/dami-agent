using Dami.Contracts.Calendar;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Calendar;

/// <summary>PostgreSQL store for the calendar mirror (migration 045).</summary>
public sealed class PostgresCalendarStore : ICalendarStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    /// <summary>Creates the store.</summary>
    public PostgresCalendarStore(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.schema = options.Value.SchemaName;
    }

    /// <inheritdoc />
    /// <remarks>One transaction: a reader never sees the window empty between the delete and the inserts.</remarks>
    public async Task ReplaceAsync(
        DateTimeOffset from, DateTimeOffset to, IReadOnlyList<CalendarEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var clear = new NpgsqlCommand(
            $"delete from {this.schema}.calendar_events where starts_at >= @from and starts_at < @to;", connection, transaction))
        {
            clear.Parameters.AddWithValue("from", from.ToUniversalTime());
            clear.Parameters.AddWithValue("to", to.ToUniversalTime());
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var item in events)
        {
            await using var insert = new NpgsqlCommand(
                $"insert into {this.schema}.calendar_events (event_key, starts_at, ends_at, all_day, summary, location, fetched_at) "
                + "values (@key, @starts, @ends, @allDay, @summary, @location, now()) on conflict (event_key) do nothing;",
                connection, transaction);
            insert.Parameters.AddWithValue("key", item.EventKey);
            insert.Parameters.AddWithValue("starts", item.StartsAt.ToUniversalTime());
            insert.Parameters.AddWithValue("ends", (object?)item.EndsAt?.ToUniversalTime() ?? DBNull.Value);
            insert.Parameters.AddWithValue("allDay", item.AllDay);
            insert.Parameters.AddWithValue("summary", item.Summary);
            insert.Parameters.AddWithValue("location", (object?)item.Location ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> BetweenAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select event_key, starts_at, ends_at, all_day, summary, location from {this.schema}.calendar_events "
            + "where starts_at >= @from and starts_at < @to order by starts_at, summary;");
        command.Parameters.AddWithValue("from", from.ToUniversalTime());
        command.Parameters.AddWithValue("to", to.ToUniversalTime());
        var events = new List<CalendarEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(new CalendarEvent(
                reader.GetString(0),
                await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false),
                await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)
                    ? null
                    : await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false),
                reader.GetBoolean(3),
                reader.GetString(4),
                await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(5)));
        }

        return events;
    }
}
