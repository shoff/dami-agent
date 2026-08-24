using System.Data;
using System.Runtime.CompilerServices;
using Dami.Contracts.Domains;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Domains;

/// <summary>The health domain in Postgres. LocalOnly — nothing here reaches the network.</summary>
public sealed class PostgresHealthEventStore : IHealthEventStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;

    /// <summary>Creates the store.</summary>
    public PostgresHealthEventStore(NpgsqlDataSource dataSource, IOptions<PostgresOptions> storeOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
    }

    private string Schema => this.storeOptions.SchemaName;

    /// <inheritdoc />
    public async Task RecordAsync(HealthEvent healthEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(healthEvent);

        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Schema}.health_events
                (health_event_id, observation_id, event_date, category, description)
            values (@id, @observation_id, @event_date, @category, @description)
            on conflict (observation_id, description) do nothing;
            """);
        command.Parameters.AddWithValue("id", healthEvent.HealthEventId);
        command.Parameters.AddWithValue("observation_id", healthEvent.ObservationId);
        command.Parameters.AddWithValue("event_date", healthEvent.EventDate);
        command.Parameters.AddWithValue("category", healthEvent.Category.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("description", healthEvent.Description);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<(Guid, DateOnly, string)> UnexaminedAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select o.observation_id, o.occurred_at, o.body
              from {this.Schema}.observations o
              left join {this.Schema}.health_examined e on e.observation_id = o.observation_id
             where e.observation_id is null
             order by o.occurred_at
             limit @limit;
            """);
        command.Parameters.AddWithValue("limit", limit);
        return StreamUnexaminedAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkExaminedAsync(Guid observationId, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Schema}.health_examined (observation_id)
            values (@id) on conflict (observation_id) do nothing;
            """);
        command.Parameters.AddWithValue("id", observationId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<HealthEvent> TimelineAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select health_event_id, observation_id, event_date, category, description
              from {this.Schema}.health_events
             order by event_date desc
             limit @limit;
            """);
        command.Parameters.AddWithValue("limit", limit);
        return StreamTimelineAsync(command, cancellationToken);
    }

    private static async IAsyncEnumerable<(Guid, DateOnly, string)> StreamUnexaminedAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var occurredAt = await reader
                    .GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false);
                yield return (
                    reader.GetGuid(0), DateOnly.FromDateTime(occurredAt.UtcDateTime), reader.GetString(2));
            }
        }
    }

    private static async IAsyncEnumerable<HealthEvent> StreamTimelineAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return new HealthEvent(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    DateOnly.FromDateTime(
                        (await reader.GetFieldValueAsync<DateTime>(2, cancellationToken)
                            .ConfigureAwait(false))),
                    Enum.Parse<HealthCategory>(reader.GetString(3), ignoreCase: true),
                    reader.GetString(4));
            }
        }
    }
}
