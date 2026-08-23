using Dami.Contracts.Capabilities;
using Dami.Persistence.Capabilities;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Capabilities;

/// <summary>The derived capability index against a live database.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresCapabilityEmbeddingStoreTests
{
    private const string MODEL = "test-model";

    private readonly DatabaseFixture fixture;

    public PostgresCapabilityEmbeddingStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task UpsertAsync_Should_Replace_An_Older_Capability_Version()
    {
        await this.fixture.ResetAsync();
        ICapabilityEmbeddingStore store = this.CreateStore();
        var capabilityId = Guid.NewGuid();
        await store.UpsertAsync(capabilityId, "1.0.0", MODEL, Unit(0), CancellationToken.None);

        await store.UpsertAsync(capabilityId, "2.0.0", MODEL, Unit(1), CancellationToken.None);

        await using var command = this.fixture.DataSource.CreateCommand(
            $"select capability_version, count(*) over () from {DatabaseFixture.SCHEMA}.capability_embeddings "
            + "where capability_id = @id and embedding_model = @model;");
        command.Parameters.AddWithValue("id", capabilityId);
        command.Parameters.AddWithValue("model", MODEL);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("2.0.0", reader.GetString(0));
        Assert.Equal(1L, reader.GetInt64(1));
    }

    [Fact]
    public async Task NearestAsync_Should_Order_Capability_Ids_By_Cosine_Distance()
    {
        await this.fixture.ResetAsync();
        ICapabilityEmbeddingStore store = this.CreateStore();
        var nearId = Guid.NewGuid();
        var farId = Guid.NewGuid();
        await store.UpsertAsync(nearId, "1.0.0", MODEL, Unit(0), CancellationToken.None);
        await store.UpsertAsync(farId, "1.0.0", MODEL, Unit(1), CancellationToken.None);

        var nearest = new List<Guid>();
        await foreach (var (capabilityId, _) in store.NearestAsync(
            Unit(0), MODEL, 2, CancellationToken.None))
        {
            nearest.Add(capabilityId);
        }

        Assert.Equal([nearId, farId], nearest);
    }

    [Fact]
    public async Task NearestAsync_Should_Search_Only_The_Requested_Model()
    {
        await this.fixture.ResetAsync();
        ICapabilityEmbeddingStore store = this.CreateStore();
        var currentId = Guid.NewGuid();
        var obsoleteId = Guid.NewGuid();
        await store.UpsertAsync(currentId, "1.0.0", MODEL, Unit(1), CancellationToken.None);
        await store.UpsertAsync(obsoleteId, "1.0.0", "obsolete-model", Unit(0), CancellationToken.None);

        var nearest = new List<Guid>();
        await foreach (var (capabilityId, _) in store.NearestAsync(
            Unit(0), MODEL, 2, CancellationToken.None))
        {
            nearest.Add(capabilityId);
        }

        Assert.Equal([currentId], nearest);
    }

    private PostgresCapabilityEmbeddingStore CreateStore()
    {
        return new PostgresCapabilityEmbeddingStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }

    private static float[] Unit(int axis)
    {
        var vector = new float[1024];
        vector[axis] = 1f;
        return vector;
    }
}
