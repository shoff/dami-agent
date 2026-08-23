using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Dami.Contracts.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Memory;

/// <summary>Belief vectors in pgvector — active set only, by construction and by trigger.</summary>
public sealed class PostgresConclusionEmbeddingStore : IConclusionEmbeddingStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;
    private readonly ILogger<PostgresConclusionEmbeddingStore> logger;

    /// <summary>Creates the store.</summary>
    public PostgresConclusionEmbeddingStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        ILogger<PostgresConclusionEmbeddingStore> logger)
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
    public IAsyncEnumerable<Conclusion> UnembeddedAsync(
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
            select c.conclusion_id, c.supersedes_id, c.subject, c.statement, c.confidence, c.source,
                   c.concluded_at, c.retracted_at, c.retraction_reason
              from {this.Schema}.conclusions c
              left join {this.Schema}.conclusion_embeddings e
                on e.conclusion_id = c.conclusion_id and e.embedding_model = @model
             where c.retracted_at is null and e.conclusion_id is null
             order by c.concluded_at
             limit @limit;
            """);
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        Guid conclusionId,
        string embeddingModel,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embeddingModel);
        ArgumentNullException.ThrowIfNull(embedding);

        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Schema}.conclusion_embeddings (conclusion_id, embedding_model, embedding)
            select @id, @model, @embedding::vector
             where exists (select 1 from {this.Schema}.conclusions
                            where conclusion_id = @id and retracted_at is null)
            on conflict (conclusion_id) do nothing;
            """);
        command.Parameters.AddWithValue("id", conclusionId);
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(embedding));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<(Conclusion Conclusion, double Distance)> NearestAsync(
        float[] queryEmbedding,
        string embeddingModel,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentNullException.ThrowIfNull(embeddingModel);
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");
        }

        var command = this.dataSource.CreateCommand(
            $"""
            select c.conclusion_id, c.supersedes_id, c.subject, c.statement, c.confidence, c.source,
                   c.concluded_at, c.retracted_at, c.retraction_reason,
                   e.embedding <=> @query::vector as distance
              from {this.Schema}.conclusion_embeddings e
              join {this.Schema}.conclusions c on c.conclusion_id = e.conclusion_id
             where e.embedding_model = @model
             order by e.embedding <=> @query::vector
             limit @limit;
            """);
        command.Parameters.AddWithValue("query", ToVectorLiteral(queryEmbedding));
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("limit", limit);
        return StreamScoredAsync(command, cancellationToken);
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

    private static async IAsyncEnumerable<Conclusion> StreamAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return Read(reader);
            }
        }
    }

    private static async IAsyncEnumerable<(Conclusion, double)> StreamScoredAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return (Read(reader), reader.GetDouble(9));
            }
        }
    }

    private static Conclusion Read(NpgsqlDataReader reader)
    {
        return new Conclusion(
            conclusionId: reader.GetGuid(0),
            supersedesId: reader.IsDBNull(1) ? null : reader.GetGuid(1),
            subject: reader.GetString(2),
            statement: reader.GetString(3),
            confidence: reader.GetDouble(4),
            source: Enum.Parse<ConclusionSource>(reader.GetString(5)),
            concludedAt: reader.GetFieldValue<DateTimeOffset>(6),
            supportingObservations: [],
            retractedAt: reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            retractionReason: reader.IsDBNull(8) ? null : reader.GetString(8));
    }
}
