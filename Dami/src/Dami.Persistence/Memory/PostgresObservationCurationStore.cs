using System.Data;
using System.Runtime.CompilerServices;
using Dami.Contracts.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Memory;

/// <summary>Finds observations worth curating and stores their rewrites.</summary>
public sealed class PostgresObservationCurationStore : IObservationCurationStore
{
    /// <summary>
    /// What "needs curating" means: transcript voice, or a date restated in prose that
    /// the row already carries as a column. Deliberately narrow — an observation that
    /// already reads naturally must be left alone, because a rewrite is a lossy edit
    /// and the only justification for one is that the text is genuinely unusable.
    /// </summary>
    private const string NEEDS_CURATION =
        @"(\mthe user\M|\mthe assistant\M|^Summary:|^Conversation summary|^As of \d{4}|^On \d{4})";

    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;

    /// <summary>Creates the store.</summary>
    public PostgresObservationCurationStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
    }

    private string Schema => this.storeOptions.SchemaName;

    /// <inheritdoc />
    public IAsyncEnumerable<Observation> UncuratedAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select o.observation_id, o.occurred_at, o.recorded_at, o.source, o.body, o.metadata
              from {this.Schema}.observations o
              left join {this.Schema}.observation_curations c using (observation_id)
             where c.observation_id is null and o.body ~* @needs
             order by o.occurred_at desc
             limit @limit;
            """);
        command.Parameters.AddWithValue("needs", NEEDS_CURATION);
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CurateAsync(
        Guid observationId,
        string curatedBody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(curatedBody);

        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Schema}.observation_curations (observation_id, curated_body, method)
            values (@id, @body, 'local-model')
            on conflict (observation_id) do nothing;
            """);
        command.Parameters.AddWithValue("id", observationId);
        command.Parameters.AddWithValue("body", curatedBody);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<Observation> StreamAsync(
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
                yield return new Observation(
                    reader.GetGuid(0),
                    await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken)
                        .ConfigureAwait(false),
                    reader.GetString(3),
                    reader.GetString(4));
            }
        }
    }
}
