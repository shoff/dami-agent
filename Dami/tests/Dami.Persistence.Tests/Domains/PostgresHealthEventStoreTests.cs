using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Persistence.Domains;
using Dami.Persistence.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Domains;

/// <summary>The health domain: provenance-anchored, idempotent, timeline-ordered.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresHealthEventStoreTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 24, 4, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresHealthEventStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task RecordAsync_Should_Not_Duplicate_The_Same_Fact_From_The_Same_Observation()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var observation = await SeedObservationAsync(corpus, "diagnosed with aortic stenosis");
        var fact = Health(observation, "severe aortic stenosis");

        await store.RecordAsync(fact, CancellationToken.None);
        await store.RecordAsync(
            new HealthEvent(Guid.NewGuid(), observation, fact.EventDate, fact.Category, fact.Description),
            CancellationToken.None);

        Assert.Single(await this.TimelineAsync(store));
    }

    [Fact]
    public async Task UnexaminedAsync_Should_Skip_An_Examined_Observation()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var examined = await SeedObservationAsync(corpus, "already looked at");
        await SeedObservationAsync(corpus, "still pending");
        await store.MarkExaminedAsync(examined, CancellationToken.None);

        var pending = await this.UnexaminedAsync(store);

        Assert.DoesNotContain(examined, pending);
    }

    [Fact]
    public async Task TimelineAsync_Should_Return_Newest_First()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var observation = await SeedObservationAsync(corpus, "cardiac history");
        await store.RecordAsync(
            new HealthEvent(Guid.NewGuid(), observation, new DateOnly(2026, 1, 30),
                HealthCategory.Diagnosis, "diagnosed"), CancellationToken.None);
        await store.RecordAsync(
            new HealthEvent(Guid.NewGuid(), observation, new DateOnly(2026, 3, 11),
                HealthCategory.Procedure, "valve replacement"), CancellationToken.None);

        var timeline = await this.TimelineAsync(store);

        Assert.Equal("valve replacement", timeline[0].Description);
    }

    private static async Task<Guid> SeedObservationAsync(IObservationCorpus corpus, string body)
    {
        var observation = new Observation(Guid.NewGuid(), at, "hermes-memory", body);
        await corpus.RecordAsync(observation, CancellationToken.None);
        return observation.ObservationId;
    }

    private static HealthEvent Health(Guid observationId, string description)
    {
        return new HealthEvent(
            Guid.NewGuid(), observationId, new DateOnly(2026, 1, 30),
            HealthCategory.Diagnosis, description);
    }

    private async Task<List<HealthEvent>> TimelineAsync(PostgresHealthEventStore store)
    {
        var events = new List<HealthEvent>();
        await foreach (var item in store.TimelineAsync(50, CancellationToken.None))
        {
            events.Add(item);
        }

        return events;
    }

    private async Task<List<Guid>> UnexaminedAsync(PostgresHealthEventStore store)
    {
        var ids = new List<Guid>();
        await foreach (var (id, _, _) in store.UnexaminedAsync(50, CancellationToken.None))
        {
            ids.Add(id);
        }

        return ids;
    }

    private (PostgresObservationCorpus, PostgresHealthEventStore) CreateStores()
    {
        var options = Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA });
        return (
            new PostgresObservationCorpus(
                this.fixture.DataSource, options, NullLogger<PostgresObservationCorpus>.Instance),
            new PostgresHealthEventStore(this.fixture.DataSource, options));
    }
}
