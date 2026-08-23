using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Dami.Contracts.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Memory;

/// <summary>Vectors over the corpus, in pgvector.</summary>
public sealed class PostgresObservationEmbeddingStore : IObservationEmbeddingStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;
    private readonly ILogger<PostgresObservationEmbeddingStore> logger;

    /// <summary>Creates the store.</summary>
    public PostgresObservationEmbeddingStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        ILogger<PostgresObservationEmbeddingStore> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
        this.logger = logger;
    }

    private string Schema => this.storeOptions.SchemaName;

    /// <inheritdoc />
    public IAsyncEnumerable<Observation> UnembeddedAsync(
        string embeddingModel,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embeddingModel);

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select o.observation_id, o.occurred_at, o.recorded_at, o.source, o.body, o.metadata
              from {this.Schema}.observations o
              left join {this.Schema}.observation_embeddings e
                on e.observation_id = o.observation_id and e.embedding_model = @model
             where e.observation_id is null
             order by o.occurred_at
             limit @limit;
            """);
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("limit", limit);
        return StreamObservationsAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        Guid observationId,
        string embeddingModel,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embeddingModel);
        ArgumentNullException.ThrowIfNull(embedding);

        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Schema}.observation_embeddings (observation_id, embedding_model, embedding)
            values (@id, @model, @embedding::vector)
            on conflict (observation_id) do nothing;
            """);
        command.Parameters.AddWithValue("id", observationId);
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(embedding));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<(Observation Observation, double Distance)> NearestAsync(
        float[] queryEmbedding,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select o.observation_id, o.occurred_at, o.recorded_at, o.source, o.body, o.metadata,
                   e.embedding <=> @query::vector as distance
              from {this.Schema}.observation_embeddings e
              join {this.Schema}.observations o on o.observation_id = e.observation_id
             order by e.embedding <=> @query::vector
             limit @limit;
            """);
        command.Parameters.AddWithValue("query", ToVectorLiteral(queryEmbedding));
        command.Parameters.AddWithValue("limit", limit);
        return StreamNearestAsync(command, cancellationToken);
    }

    private static string ToVectorLiteral(float[] embedding)
    {
        var literal = new StringBuilder("[");
        for (var index = 0; index < embedding.Length; index++)
        {
            if (index > 0)
            {
                literal.Append(',');
            }

            literal.Append(embedding[index].ToString(CultureInfo.InvariantCulture));
        }

        return literal.Append(']').ToString();
    }

    private static async IAsyncEnumerable<Observation> StreamObservationsAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return ReadObservation(reader);
            }
        }
    }

    private static async IAsyncEnumerable<(Observation, double)> StreamNearestAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return (ReadObservation(reader), reader.GetDouble(6));
            }
        }
    }

    private static Observation ReadObservation(NpgsqlDataReader reader)
    {
        var metadata = reader.IsDBNull(5)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5));

        return new Observation(
            observationId: reader.GetGuid(0),
            occurredAt: reader.GetFieldValue<DateTimeOffset>(1),
            source: reader.GetString(3),
            body: reader.GetString(4),
            metadata: metadata,
            recordedAt: reader.GetFieldValue<DateTimeOffset>(2));
    }
}
