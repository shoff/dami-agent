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
}
