using Dami.Contracts.Memory;
using Dami.Persistence.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Memory;

/// <summary>The semantic index against a live database.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresObservationEmbeddingStoreTests
{
    private const string MODEL = "test-model";
    private static readonly DateTimeOffset occurredAt = new(2026, 8, 23, 5, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresObservationEmbeddingStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public void Constructor_Should_Reject_A_Null_DataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresObservationEmbeddingStore(
            null!, Options.Create(new PostgresOptions()),
            NullLogger<PostgresObservationEmbeddingStore>.Instance));
    }

    [Fact]
    public async Task UnembeddedAsync_Should_Return_Observations_Without_Vectors()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        await corpus.RecordAsync(Observed("not yet indexed"), CancellationToken.None);

        Assert.Single(await this.UnembeddedAsync(store));
    }

    [Fact]
    public async Task StoreAsync_Should_Remove_The_Observation_From_The_Unembedded_Set()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var observation = Observed("gets a vector");
        await corpus.RecordAsync(observation, CancellationToken.None);

        await store.StoreAsync(observation.ObservationId, MODEL, Unit(0), CancellationToken.None);

        Assert.Empty(await this.UnembeddedAsync(store));
    }

    [Fact]
    public async Task UnembeddedAsync_Should_Treat_A_Different_Model_As_Unembedded()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var observation = Observed("indexed under another model");
        await corpus.RecordAsync(observation, CancellationToken.None);
        await store.StoreAsync(observation.ObservationId, "other-model", Unit(0), CancellationToken.None);

        Assert.Single(await this.UnembeddedAsync(store));
    }

    [Fact]
    public async Task StoreAsync_Should_Keep_A_Vector_For_Each_Model()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var observation = Observed("re-embedded under a new model");
        await corpus.RecordAsync(observation, CancellationToken.None);
        await store.StoreAsync(observation.ObservationId, MODEL, Unit(0), CancellationToken.None);
        await store.StoreAsync(observation.ObservationId, "replacement-model", Unit(1), CancellationToken.None);

        await using var command = this.fixture.DataSource.CreateCommand(
            $"select count(*) from {DatabaseFixture.SCHEMA}.observation_embeddings "
            + "where observation_id = @id;");
        command.Parameters.AddWithValue("id", observation.ObservationId);

        Assert.Equal(2L, await command.ExecuteScalarAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NearestAsync_Should_Order_By_Cosine_Distance()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var near = Observed("near");
        var far = Observed("far");
        await corpus.RecordAsync(near, CancellationToken.None);
        await corpus.RecordAsync(far, CancellationToken.None);
        await store.StoreAsync(near.ObservationId, MODEL, Unit(0), CancellationToken.None);
        await store.StoreAsync(far.ObservationId, MODEL, Unit(1), CancellationToken.None);

        var nearest = new List<Observation>();
        await foreach (var (observation, _) in store.NearestAsync(
            Unit(0), MODEL, 2, CancellationToken.None))
        {
            nearest.Add(observation);
        }

        Assert.Equal("near", nearest[0].Body);
    }

    [Fact]
    public async Task NearestAsync_Should_Search_Only_The_Requested_Model()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var current = Observed("current model");
        var obsolete = Observed("obsolete model");
        await corpus.RecordAsync(current, CancellationToken.None);
        await corpus.RecordAsync(obsolete, CancellationToken.None);
        await store.StoreAsync(current.ObservationId, MODEL, Unit(1), CancellationToken.None);
        await store.StoreAsync(obsolete.ObservationId, "obsolete-model", Unit(0), CancellationToken.None);

        var nearest = new List<Observation>();
        await foreach (var (observation, _) in store.NearestAsync(
            Unit(0), MODEL, 2, CancellationToken.None))
        {
            nearest.Add(observation);
        }

        Assert.Equal(["current model"], nearest.Select(observation => observation.Body));
    }

    private static Observation Observed(string body)
    {
        return new Observation(Guid.NewGuid(), occurredAt, "test", body);
    }

    private static float[] Unit(int axis)
    {
        var vector = new float[1024];
        vector[axis] = 1f;
        return vector;
    }

    private async Task<List<Observation>> UnembeddedAsync(IObservationEmbeddingStore store)
    {
        var pending = new List<Observation>();
        await foreach (var observation in store.UnembeddedAsync(MODEL, 10, CancellationToken.None))
        {
            pending.Add(observation);
        }

        return pending;
    }

    private (PostgresObservationCorpus, PostgresObservationEmbeddingStore) CreateStores()
    {
        var options = Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA });
        return (
            new PostgresObservationCorpus(
                this.fixture.DataSource, options, NullLogger<PostgresObservationCorpus>.Instance),
            new PostgresObservationEmbeddingStore(
                this.fixture.DataSource, options, NullLogger<PostgresObservationEmbeddingStore>.Instance));
    }
}
