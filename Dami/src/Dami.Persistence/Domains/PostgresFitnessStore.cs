using System.Data;
using Dami.Contracts.Domains;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Domains;

/// <summary>The fitness domain in Postgres (H9/G14). LocalOnly — no egress path exists.</summary>
public sealed class PostgresFitnessStore : IFitnessStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;

    /// <summary>Creates the store.</summary>
    public PostgresFitnessStore(NpgsqlDataSource dataSource, IOptions<PostgresOptions> storeOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
    }

    private string Schema => this.storeOptions.SchemaName;

    /// <inheritdoc />
    public async Task<FitnessSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        return new FitnessSnapshot(
            await this.CardioAsync(cancellationToken).ConfigureAwait(false),
            await this.SetsAsync(cancellationToken).ConfigureAwait(false),
            await this.WeighInsAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<FitnessCardioSession>> CardioAsync(
        CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select e.fitness_event_id, e.occurred_at, c.modality, c.duration_seconds,
                   c.distance_mi, c.calories, c.hr_avg, c.hr_max, c.is_pr, c.notes
              from {this.Schema}.fitness_cardio c
              join {this.Schema}.fitness_event e using (fitness_event_id)
             order by e.occurred_at;
            """);
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

        var sessions = new List<FitnessCardioSession>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions.Add(await ReadCardioAsync(reader, cancellationToken).ConfigureAwait(false));
        }

        return sessions;
    }

    private static async Task<FitnessCardioSession> ReadCardioAsync(
        NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        return new FitnessCardioSession(
            reader.GetGuid(0),
            await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false),
            reader.GetString(2),
            await reader.GetFieldValueAsync<int?>(3, cancellationToken).ConfigureAwait(false),
            await reader.GetFieldValueAsync<decimal?>(4, cancellationToken).ConfigureAwait(false),
            await reader.GetFieldValueAsync<int?>(5, cancellationToken).ConfigureAwait(false),
            await reader.GetFieldValueAsync<int?>(6, cancellationToken).ConfigureAwait(false),
            await reader.GetFieldValueAsync<int?>(7, cancellationToken).ConfigureAwait(false),
            reader.GetBoolean(8),
            await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false)
                ? null
                : reader.GetString(9));
    }

    private async Task<IReadOnlyList<FitnessSet>> SetsAsync(CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select s.set_id, s.fitness_event_id, e.occurred_at,
                   coalesce(x.name, 'unrecorded exercise'), x.primary_muscle_group,
                   s.set_number, s.reps, s.weight_lbs, s.rpe, s.is_warmup
              from {this.Schema}.fitness_resistance_set s
              join {this.Schema}.fitness_event e using (fitness_event_id)
              left join {this.Schema}.fitness_exercise x on x.exercise_id = s.exercise_id
             order by e.occurred_at, s.set_number;
            """);
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

        var sets = new List<FitnessSet>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sets.Add(await ReadSetAsync(reader, cancellationToken).ConfigureAwait(false));
        }

        return sets;
    }

    private static async Task<FitnessSet> ReadSetAsync(
        NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        return new FitnessSet(
            reader.GetGuid(0),
            reader.GetGuid(1),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false),
            reader.GetString(3),
            await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false)
                ? null
                : reader.GetString(4),
            reader.GetInt16(5),
            await reader.GetFieldValueAsync<short?>(6, cancellationToken).ConfigureAwait(false),
            await reader.GetFieldValueAsync<decimal?>(7, cancellationToken).ConfigureAwait(false),
            await reader.GetFieldValueAsync<short?>(8, cancellationToken).ConfigureAwait(false),
            reader.GetBoolean(9));
    }

    private async Task<IReadOnlyList<FitnessWeighIn>> WeighInsAsync(CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select e.occurred_at, w.weight_lbs
              from {this.Schema}.fitness_weight w
              join {this.Schema}.fitness_event e using (fitness_event_id)
             order by e.occurred_at;
            """);
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

        var weighIns = new List<FitnessWeighIn>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            weighIns.Add(new FitnessWeighIn(
                await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken).ConfigureAwait(false),
                reader.GetDecimal(1)));
        }

        return weighIns;
    }

    /// <inheritdoc />
    public async Task<Guid> RecordResistanceAsync(FitnessResistanceEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Exercise);
        if (entry.Sets.Count == 0)
        {
            throw new ArgumentException("A resistance entry needs at least one set.", nameof(entry));
        }

        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var eventId = Guid.NewGuid();
        await this.InsertEventAsync(connection, eventId, entry.OccurredAt, "resistance", entry.Source, entry.Notes, cancellationToken)
            .ConfigureAwait(false);
        await using (var resistance = new NpgsqlCommand(
            $"insert into {this.Schema}.fitness_resistance (fitness_event_id, notes) values (@id, @notes)", connection))
        {
            resistance.Parameters.AddWithValue("id", eventId);
            resistance.Parameters.AddWithValue("notes", (object?)entry.Notes ?? DBNull.Value);
            await resistance.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var exerciseId = await this.ExerciseIdAsync(connection, entry, cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < entry.Sets.Count; index++)
        {
            await InsertSetAsync(connection, this.Schema, eventId, exerciseId, (short)(index + 1), entry.Sets[index], cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return eventId;
    }

    /// <inheritdoc />
    public async Task<Guid> RecordCardioAsync(FitnessCardioEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Modality);
        await using var connection = await this.dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var eventId = Guid.NewGuid();
        await this.InsertEventAsync(connection, eventId, entry.OccurredAt, "cardio", entry.Source, entry.Notes, cancellationToken)
            .ConfigureAwait(false);
        await using (var cardio = new NpgsqlCommand(
            $"""
            insert into {this.Schema}.fitness_cardio
                (fitness_event_id, modality, duration_seconds, distance_mi, calories, speed_mph, incline_pct, hr_avg, hr_max, notes)
            values (@id, @modality, @duration, @distance, @calories, @speed, @incline, @hr_avg, @hr_max, @notes)
            """, connection))
        {
            cardio.Parameters.AddWithValue("id", eventId);
            cardio.Parameters.AddWithValue("modality", entry.Modality);
            cardio.Parameters.AddWithValue("duration", (object?)entry.DurationSeconds ?? DBNull.Value);
            cardio.Parameters.AddWithValue("distance", (object?)entry.DistanceMi ?? DBNull.Value);
            cardio.Parameters.AddWithValue("calories", (object?)entry.Calories ?? DBNull.Value);
            cardio.Parameters.AddWithValue("speed", (object?)entry.SpeedMph ?? DBNull.Value);
            cardio.Parameters.AddWithValue("incline", (object?)entry.InclinePct ?? DBNull.Value);
            cardio.Parameters.AddWithValue("hr_avg", (object?)entry.HrAvg ?? DBNull.Value);
            cardio.Parameters.AddWithValue("hr_max", (object?)entry.HrMax ?? DBNull.Value);
            cardio.Parameters.AddWithValue("notes", (object?)entry.Notes ?? DBNull.Value);
            await cardio.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return eventId;
    }

    private async Task InsertEventAsync(
        NpgsqlConnection connection, Guid eventId, DateTimeOffset occurredAt, string kind, string source, string? notes,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.Schema}.fitness_event
                (fitness_event_id, occurred_at, kind, precision, sensitivity, source, notes, verified_by_user)
            values (@id, @at, @kind, 'exact', 'normal', @source, @notes, true)
            """, connection);
        command.Parameters.AddWithValue("id", eventId);
        command.Parameters.AddWithValue("at", occurredAt);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The exercise by name, case-insensitively, created on first sight.</summary>
    private async Task<int> ExerciseIdAsync(
        NpgsqlConnection connection, FitnessResistanceEntry entry, CancellationToken cancellationToken)
    {
        await using var find = new NpgsqlCommand(
            $"select exercise_id from {this.Schema}.fitness_exercise where lower(name) = lower(@name)", connection);
        find.Parameters.AddWithValue("name", entry.Exercise.Trim());
        if (await find.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is int existing)
        {
            return existing;
        }

        await using var create = new NpgsqlCommand(
            $"""
            insert into {this.Schema}.fitness_exercise (exercise_id, name, primary_muscle_group, equipment)
            select coalesce(max(exercise_id), 0) + 1, @name, @muscle, @equipment from {this.Schema}.fitness_exercise
            returning exercise_id
            """, connection);
        create.Parameters.AddWithValue("name", entry.Exercise.Trim());
        create.Parameters.AddWithValue("muscle", (object?)entry.MuscleGroup ?? DBNull.Value);
        create.Parameters.AddWithValue("equipment", (object?)entry.Equipment ?? DBNull.Value);
        return (int)(await create.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task InsertSetAsync(
        NpgsqlConnection connection, string schema, Guid eventId, int exerciseId, short number, FitnessSetEntry set,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {schema}.fitness_resistance_set
                (set_id, fitness_event_id, exercise_id, set_number, reps, weight_lbs, rpe, is_warmup)
            values (@id, @event, @exercise, @number, @reps, @weight, @rpe, @warmup)
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("event", eventId);
        command.Parameters.AddWithValue("exercise", exerciseId);
        command.Parameters.AddWithValue("number", number);
        command.Parameters.AddWithValue("reps", (object?)set.Reps ?? DBNull.Value);
        command.Parameters.AddWithValue("weight", (object?)set.WeightLbs ?? DBNull.Value);
        command.Parameters.AddWithValue("rpe", (object?)set.Rpe ?? DBNull.Value);
        command.Parameters.AddWithValue("warmup", set.IsWarmup);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
