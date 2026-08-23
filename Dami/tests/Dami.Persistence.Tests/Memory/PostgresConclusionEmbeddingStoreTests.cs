using Dami.Contracts.Memory;
using Dami.Persistence.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Memory;

/// <summary>Belief vectors: active-only, and retraction takes the vector with it.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresConclusionEmbeddingStoreTests
{
    private const string MODEL = "test-model";
    private static readonly DateTimeOffset at = new(2026, 8, 23, 17, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresConclusionEmbeddingStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task UnembeddedAsync_Should_See_Only_Active_Conclusions()
    {
        await this.fixture.ResetAsync();
        var (ledger, store) = this.CreateStores();
        var active = Believe("active belief");
        await ledger.RecordAsync(active, CancellationToken.None);
        var retracted = Believe("retracted belief");
        await ledger.RecordAsync(retracted, CancellationToken.None);
        await ledger.RetractAsync(retracted.ConclusionId, "wrong", at, CancellationToken.None);

        Assert.Single(await this.UnembeddedAsync(store));
    }

    [Fact]
    public async Task Retraction_Should_Remove_The_Vector_Atomically()
    {
        await this.fixture.ResetAsync();
        var (ledger, store) = this.CreateStores();
        var belief = Believe("soon to be retracted");
        await ledger.RecordAsync(belief, CancellationToken.None);
        await store.StoreAsync(belief.ConclusionId, MODEL, Unit(0), CancellationToken.None);

        await ledger.RetractAsync(belief.ConclusionId, "changed", at, CancellationToken.None);

        Assert.Empty(await this.NearestAsync(store));
    }

    [Fact]
    public async Task StoreAsync_Should_Refuse_A_Vector_For_A_Retracted_Conclusion()
    {
        await this.fixture.ResetAsync();
        var (ledger, store) = this.CreateStores();
        var belief = Believe("already dead");
        await ledger.RecordAsync(belief, CancellationToken.None);
        await ledger.RetractAsync(belief.ConclusionId, "gone", at, CancellationToken.None);

        await store.StoreAsync(belief.ConclusionId, MODEL, Unit(0), CancellationToken.None);

        Assert.Empty(await this.NearestAsync(store));
    }

    [Fact]
    public async Task NearestAsync_Should_Order_By_Distance()
    {
        await this.fixture.ResetAsync();
        var (ledger, store) = this.CreateStores();
        var near = Believe("near belief");
        var far = Believe("far belief");
        await ledger.RecordAsync(near, CancellationToken.None);
        await ledger.RecordAsync(far, CancellationToken.None);
        await store.StoreAsync(near.ConclusionId, MODEL, Unit(0), CancellationToken.None);
        await store.StoreAsync(far.ConclusionId, MODEL, Unit(1), CancellationToken.None);

        var nearest = await this.NearestAsync(store);

        Assert.Equal("near belief", nearest[0].Statement);
    }

    private static Conclusion Believe(string statement)
    {
        return new Conclusion(
            Guid.NewGuid(), null, "steve", statement, 0.8, ConclusionSource.ReflectionPass, at);
    }

    private static float[] Unit(int axis)
    {
        var vector = new float[1024];
        vector[axis] = 1f;
        return vector;
    }

    private async Task<List<Conclusion>> UnembeddedAsync(IConclusionEmbeddingStore store)
    {
        var pending = new List<Conclusion>();
        await foreach (var conclusion in store.UnembeddedAsync(MODEL, 10, CancellationToken.None))
        {
            pending.Add(conclusion);
        }

        return pending;
    }

    private async Task<List<Conclusion>> NearestAsync(IConclusionEmbeddingStore store)
    {
        var found = new List<Conclusion>();
        await foreach (var (conclusion, _) in store.NearestAsync(Unit(0), MODEL, 10, CancellationToken.None))
        {
            found.Add(conclusion);
        }

        return found;
    }

    private (PostgresConclusionLedger, PostgresConclusionEmbeddingStore) CreateStores()
    {
        var options = Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA });
        return (
            new PostgresConclusionLedger(
                this.fixture.DataSource, options, NullLogger<PostgresConclusionLedger>.Instance),
            new PostgresConclusionEmbeddingStore(
                this.fixture.DataSource, options, NullLogger<PostgresConclusionEmbeddingStore>.Instance));
    }
}
