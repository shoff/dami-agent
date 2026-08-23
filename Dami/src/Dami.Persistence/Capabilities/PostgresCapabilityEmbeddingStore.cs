using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Dami.Contracts.Capabilities;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Capabilities;

/// <summary>Derived capability-description vectors in PostgreSQL.</summary>
public sealed class PostgresCapabilityEmbeddingStore : ICapabilityEmbeddingStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;

    /// <summary>Creates the capability index store.</summary>
    public PostgresCapabilityEmbeddingStore(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
    }

    private string Table => $"{this.storeOptions.SchemaName}.capability_embeddings";

    /// <inheritdoc />
    public async Task UpsertAsync(
        Guid capabilityId,
        string capabilityVersion,
        string embeddingModel,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capabilityVersion);
        ArgumentNullException.ThrowIfNull(embeddingModel);
        ArgumentNullException.ThrowIfNull(embedding);

        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.Table} "
            + "(capability_id, capability_version, embedding_model, embedding) "
            + "values (@id, @version, @model, @embedding::vector) "
            + "on conflict (capability_id, embedding_model) do update "
            + "set capability_version = excluded.capability_version, "
            + "embedded_at = now(), embedding = excluded.embedding;");
        command.Parameters.AddWithValue("id", capabilityId);
        command.Parameters.AddWithValue("version", capabilityVersion);
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(embedding));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<(Guid CapabilityId, double Distance)> NearestAsync(
        float[] queryEmbedding,
        string embeddingModel,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentNullException.ThrowIfNull(embeddingModel);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);

        var command = this.dataSource.CreateCommand(
            $"select capability_id, embedding <=> @query::vector as distance from {this.Table} "
            + "where embedding_model = @model order by embedding <=> @query::vector limit @limit;");
        command.Parameters.AddWithValue("query", ToVectorLiteral(queryEmbedding));
        command.Parameters.AddWithValue("model", embeddingModel);
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

    private static async IAsyncEnumerable<(Guid CapabilityId, double Distance)> StreamNearestAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return (reader.GetGuid(0), reader.GetDouble(1));
            }
        }
    }
}
