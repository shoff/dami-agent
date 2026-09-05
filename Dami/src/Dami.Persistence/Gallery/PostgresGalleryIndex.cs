using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Dami.Contracts.Gallery;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Gallery;

/// <summary>The Gallery index over PostgreSQL and pgvector (migration 040).</summary>
public sealed class PostgresGalleryIndex : IGalleryIndex
{
    private const string COLUMNS =
        "file_name, created_at, source, prompt, model, is_canonical, caption, tags, caption_model, captioned_at, derived_from, favourite, hidden";

    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;

    /// <summary>Creates the index.</summary>
    public PostgresGalleryIndex(NpgsqlDataSource dataSource, IOptions<PostgresOptions> storeOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
    }

    private string Images => $"{this.storeOptions.SchemaName}.gallery_images";

    private string Embeddings => $"{this.storeOptions.SchemaName}.gallery_image_embeddings";

    /// <inheritdoc />
    public async Task UpsertAsync(GalleryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Images} (file_name, created_at, source, prompt, model, is_canonical, derived_from)
            values (@file, @created, @source, @prompt, @model, @canonical, @derived)
            on conflict (file_name) do update
               set created_at = excluded.created_at, source = excluded.source, prompt = excluded.prompt,
                   model = excluded.model, is_canonical = excluded.is_canonical,
                   derived_from = coalesce(excluded.derived_from, {this.Images}.derived_from);
            """);
        command.Parameters.AddWithValue("file", entry.FileName);
        command.Parameters.AddWithValue("created", entry.CreatedAt);
        command.Parameters.AddWithValue("source", entry.Source.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("prompt", entry.Prompt);
        command.Parameters.AddWithValue("model", entry.Model);
        command.Parameters.AddWithValue("canonical", entry.IsCanonical);
        command.Parameters.AddWithValue("derived", (object?)entry.DerivedFrom ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GalleryEntry?> FindAsync(string fileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        await using var command = this.dataSource.CreateCommand(
            $"select {COLUMNS} from {this.Images} where file_name = @file");
        command.Parameters.AddWithValue("file", fileName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<GalleryEntry> ListAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);
        var command = this.dataSource.CreateCommand(
            $"select {COLUMNS} from {this.Images} order by created_at desc limit @limit");
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetFlagsAsync(string fileName, bool? favourite, bool? hidden, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        await using var command = this.dataSource.CreateCommand(
            $"update {this.Images} set favourite = coalesce(@favourite, favourite), hidden = coalesce(@hidden, hidden) where file_name = @file");
        command.Parameters.AddWithValue("file", fileName);
        command.Parameters.AddWithValue("favourite", (object?)favourite ?? DBNull.Value);
        command.Parameters.AddWithValue("hidden", (object?)hidden ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new KeyNotFoundException($"Gallery picture {fileName} is not indexed.");
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<GalleryEntry> UncaptionedAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);
        var command = this.dataSource.CreateCommand(
            $"select {COLUMNS} from {this.Images} where caption is null and not hidden order by indexed_at limit @limit");
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DescribeAsync(string fileName, GalleryDescription description, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(description);
        await using var command = this.dataSource.CreateCommand(
            $"update {this.Images} set caption = @caption, tags = @tags::jsonb, caption_model = @model, captioned_at = @at where file_name = @file");
        command.Parameters.AddWithValue("file", fileName);
        command.Parameters.AddWithValue("caption", description.Caption);
        command.Parameters.AddWithValue("tags", JsonSerializer.Serialize(description.Tags));
        command.Parameters.AddWithValue("model", description.CaptionModel);
        command.Parameters.AddWithValue("at", description.At);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new KeyNotFoundException($"Gallery picture {fileName} is not indexed.");
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<GalleryEntry> UnembeddedAsync(string embeddingModel, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);
        var command = this.dataSource.CreateCommand(
            $"""
            select {Qualified("i")} from {this.Images} i
              left join {this.Embeddings} e on e.file_name = i.file_name and e.embedding_model = @model
             where i.caption is not null and e.file_name is null
             order by i.captioned_at limit @limit
            """);
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StoreEmbeddingAsync(
        string fileName, string embeddingModel, float[] embedding, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentNullException.ThrowIfNull(embedding);
        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.Embeddings} (file_name, embedding_model, embedding)
            values (@file, @model, @embedding::vector)
            on conflict (file_name, embedding_model) do update set embedding = excluded.embedding, embedded_at = now();
            """);
        command.Parameters.AddWithValue("file", fileName);
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(embedding));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<(GalleryEntry Entry, double Distance)> NearestToAsync(
        string fileName, string embeddingModel, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);
        var command = this.dataSource.CreateCommand(
            $"""
            with anchor as (
                select embedding from {this.Embeddings} where file_name = @file and embedding_model = @model)
            select {Qualified("i")}, e.embedding <=> anchor.embedding as distance
              from {this.Embeddings} e
              join {this.Images} i on i.file_name = e.file_name
              cross join anchor
             where e.embedding_model = @model and not i.hidden and e.file_name <> @file
             order by e.embedding <=> anchor.embedding
             limit @limit
            """);
        command.Parameters.AddWithValue("file", fileName);
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("limit", limit);
        return StreamNearestAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<(GalleryEntry Entry, double Distance)> NearestAsync(
        float[] queryEmbedding, string embeddingModel, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);
        var command = this.dataSource.CreateCommand(
            $"""
            select {Qualified("i")}, e.embedding <=> @query::vector as distance
              from {this.Embeddings} e
              join {this.Images} i on i.file_name = e.file_name
             where e.embedding_model = @model and not i.hidden
             order by e.embedding <=> @query::vector
             limit @limit
            """);
        command.Parameters.AddWithValue("query", ToVectorLiteral(queryEmbedding));
        command.Parameters.AddWithValue("model", embeddingModel);
        command.Parameters.AddWithValue("limit", limit);
        return StreamNearestAsync(command, cancellationToken);
    }

    private static string Qualified(string alias) =>
        string.Join(", ", COLUMNS.Split(", ").Select(column => alias + "." + column));

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

    private static async IAsyncEnumerable<GalleryEntry> StreamAsync(
        NpgsqlCommand command, [EnumeratorCancellation] CancellationToken cancellationToken)
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

    private static async IAsyncEnumerable<(GalleryEntry, double)> StreamNearestAsync(
        NpgsqlCommand command, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return (Read(reader), reader.GetDouble(13));
            }
        }
    }

    private static GalleryEntry Read(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetFieldValue<DateTimeOffset>(1),
        Enum.Parse<GallerySource>(reader.GetString(2), ignoreCase: true),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetBoolean(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : JsonSerializer.Deserialize<string[]>(reader.GetString(7)),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.GetBoolean(11),
        reader.GetBoolean(12));
}
