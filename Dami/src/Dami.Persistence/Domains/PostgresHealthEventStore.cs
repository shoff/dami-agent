using System.Data;
using System.Runtime.CompilerServices;
using Dami.Contracts.Context;
using Dami.Contracts.Domains;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Domains;

/// <summary>The health domain in Postgres. LocalOnly — nothing here reaches the network.</summary>
public sealed class PostgresHealthEventStore : IHealthEventStore, IStructuredFactSource
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

    /// <summary>Terms that make an observation worth examining sooner. Recall over
    /// precision: a false positive costs one model call, a false negative costs months.</summary>
    private const string HEALTH_TERMS =
        "\\m(diagnos|surgery|surgeon|cardio|aortic|stenosis|valve|heart|blood pressure|"
        + "medication|prescri|dose|mg\\M|symptom|pain|doctor|physician|clinic|hospital|"
        + "appointment|echo|ekg|ecg|lab result|cholesterol|dizz|fatigue|nausea|procedure|"
        + "anesthe|recovery|rehab|therapy|allerg|vaccin|weight|bpm|pulse)";

    private string Schema => this.storeOptions.SchemaName;

    /// <inheritdoc />
    public async Task RecordAsync(HealthEvent healthEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(healthEvent);

        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Schema}.health_events
                (health_event_id, observation_id, event_date, category, description)
            select @id, @observation_id, @event_date, @category, @description
             where not exists (
                 select 1 from {this.Schema}.health_event_rejections
                  where observation_id = @observation_id and description = @description)
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
            select o.observation_id,
                   coalesce(r.repaired_occurred_at, o.occurred_at) as occurred_at,
                   o.body
              from {this.Schema}.observations o
              left join {this.Schema}.health_examined e on e.observation_id = o.observation_id
              left join {this.Schema}.observation_date_repairs r on r.observation_id = o.observation_id
             where e.observation_id is null
             -- Likely-medical notes first. Every observation is still examined
             -- eventually, but oldest-first alone would spend months on unrelated
             -- history before the timeline held anything worth reading. The filter is
             -- a cheap SQL prefilter, never a decision: the model still judges, and a
             -- note that merely mentions a term can still yield nothing.
             order by (o.body ~* @health_terms) desc, o.occurred_at
             limit @limit;
            """);
        command.Parameters.AddWithValue("health_terms", HEALTH_TERMS);
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
    public async Task RejectAsync(
        Guid observationId,
        string description,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(reason);

        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Schema}.health_event_rejections (observation_id, description, reason)
            values (@observation_id, @description, @reason)
            on conflict (observation_id, description) do nothing;

            delete from {this.Schema}.health_events
             where observation_id = @observation_id and description = @description;
            """);
        command.Parameters.AddWithValue("observation_id", observationId);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("reason", reason);
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
            -- The same fact is stated in many notes ("aortic stenosis" appears in
            -- dozens), and each note is a separate observation, so the per-observation
            -- uniqueness constraint cannot collapse them. Deduplicate on the wording
            -- at read time and keep the EARLIEST occurrence, which is when the fact
            -- entered the record — the timeline should say when something became true,
            -- not when it was last mentioned.
            select distinct on (lower(btrim(h.description)))
                   h.health_event_id, h.observation_id, h.event_date, h.category, h.description
              from {this.Schema}.health_events h
             where not exists (
                 select 1 from {this.Schema}.health_event_rejections r
                  where r.observation_id = h.observation_id and r.description = h.description)
             -- A dated occurrence always beats an undated one: epoch-zero means
             -- "unknown", not "earliest", and letting it win the tie-break stamps
             -- 1970 on facts whose real date is recorded elsewhere.
             order by lower(btrim(h.description)),
                      (h.event_date < date '1971-01-01'),
                      h.event_date
             limit @limit;
            """);
        command.Parameters.AddWithValue("limit", limit);
        return StreamTimelineAsync(command, cancellationToken);
    }

    /// <summary>What extraction writes when it has no date and the column forbids null.</summary>
    private static readonly DateOnly undated = new(1970, 1, 1);

    /// <inheritdoc />
    public string Domain => "health";

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately returns the timeline rather than matching the request's words against it.
    /// Word overlap cannot connect "heart" to "aortic stenosis", and the caller has already
    /// decided this domain bears on the question; the domain's job is to hand over what it
    /// holds, compactly and newest first, not to second-guess that decision.
    ///
    /// Deduplication and ordering have to happen in separate passes. DISTINCT ON requires
    /// its ORDER BY to lead with the dedupe key, so ordering and limiting in the same query
    /// takes the alphabetically first rows: asked about a heart condition it returned
    /// "aortic stenosis", "Autism spectrum disorder", "average heart rate", "bowel
    /// obstruction" — an A-to-B slice of the timeline rather than the recent facts.
    /// </remarks>
    public IAsyncEnumerable<StructuredFact> RelevantAsync(
        string request,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select observation_id, description, event_date, category
              from (select distinct on (lower(btrim(h.description)))
                           h.observation_id, h.description, h.event_date, h.category
                      from {this.Schema}.health_events h
                     where not exists (
                         select 1 from {this.Schema}.health_event_rejections r
                          where r.observation_id = h.observation_id
                            and r.description = h.description)
                     order by lower(btrim(h.description)), h.event_date desc) distinct_facts
             order by event_date desc
             limit @limit;
            """);
        command.Parameters.AddWithValue("limit", limit);
        return StreamFactsAsync(command, cancellationToken);
    }

    private static async IAsyncEnumerable<StructuredFact> StreamFactsAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var occurred = DateOnly.FromDateTime(
                    await reader.GetFieldValueAsync<DateTime>(2, cancellationToken).ConfigureAwait(false));

                yield return new StructuredFact(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    occurred == undated ? null : occurred,
                    reader.GetString(3));
            }
        }
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
