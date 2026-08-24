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
    public async Task UnexaminedAsync_Should_See_A_Repaired_Date_Not_Epoch_Zero()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var epochZero = new Observation(
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, "hermes-memory", "diagnosed 2026-01-30");
        await corpus.RecordAsync(epochZero, CancellationToken.None);
        await this.RepairAsync(epochZero.ObservationId, new DateOnly(2026, 1, 30));

        DateOnly? seen = null;
        await foreach (var (id, occurredOn, _) in store.UnexaminedAsync(50, CancellationToken.None))
        {
            if (id == epochZero.ObservationId)
            {
                seen = occurredOn;
            }
        }

        Assert.Equal(new DateOnly(2026, 1, 30), seen);
    }

    private async Task RepairAsync(Guid observationId, DateOnly repairedTo)
    {
        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            insert into {DatabaseFixture.SCHEMA}.observation_date_repairs
                (observation_id, repaired_occurred_at, method)
            values (@id, @at, 'body-iso');
            """);
        command.Parameters.AddWithValue("id", observationId);
        command.Parameters.AddWithValue("at", repairedTo.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task UnexaminedAsync_Should_Offer_Likely_Medical_Notes_First()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        // The unrelated note is OLDER, so plain oldest-first would return it first.
        var unrelated = new Observation(
            Guid.NewGuid(), at.AddYears(-1), "hermes-memory", "repainted the workshop shelves");
        await corpus.RecordAsync(unrelated, CancellationToken.None);
        var medical = new Observation(
            Guid.NewGuid(), at, "hermes-memory", "the surgeon confirmed severe aortic stenosis");
        await corpus.RecordAsync(medical, CancellationToken.None);

        var pending = await this.UnexaminedAsync(store);

        Assert.Equal(medical.ObservationId, pending[0]);
    }

    [Fact]
    public async Task TimelineAsync_Should_Collapse_The_Same_Fact_Stated_Twice()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        // The same condition stated in two different notes: two observations, so the
        // per-observation constraint cannot collapse them.
        var first = await SeedObservationAsync(corpus, "note one");
        var second = await SeedObservationAsync(corpus, "note two");
        await store.RecordAsync(
            new HealthEvent(Guid.NewGuid(), first, new DateOnly(2026, 1, 30),
                HealthCategory.Diagnosis, "Severe aortic stenosis"), CancellationToken.None);
        await store.RecordAsync(
            new HealthEvent(Guid.NewGuid(), second, new DateOnly(2026, 3, 4),
                HealthCategory.Diagnosis, "severe aortic stenosis "), CancellationToken.None);

        var timeline = await this.TimelineAsync(store);

        Assert.Single(timeline);
    }

    [Fact]
    public async Task TimelineAsync_Should_Prefer_A_Dated_Occurrence_Over_An_Undated_One()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var undatedSource = await SeedObservationAsync(corpus, "an undated note");
        var datedSource = await SeedObservationAsync(corpus, "a dated note");
        // Epoch zero means "unknown", not "earliest" — it must not win the tie-break.
        await store.RecordAsync(
            new HealthEvent(Guid.NewGuid(), undatedSource, new DateOnly(1970, 1, 1),
                HealthCategory.Procedure, "Mechanical AVR surgery"), CancellationToken.None);
        await store.RecordAsync(
            new HealthEvent(Guid.NewGuid(), datedSource, new DateOnly(2026, 3, 11),
                HealthCategory.Procedure, "Mechanical AVR surgery"), CancellationToken.None);

        var timeline = await this.TimelineAsync(store);

        Assert.Equal(new DateOnly(2026, 3, 11), Assert.Single(timeline).EventDate);
    }

    [Fact]
    public async Task TimelineAsync_Should_Keep_The_Earliest_Occurrence_Of_A_Fact()
    {
        await this.fixture.ResetAsync();
        var (corpus, store) = this.CreateStores();
        var first = await SeedObservationAsync(corpus, "note one");
        var second = await SeedObservationAsync(corpus, "note two");
        await store.RecordAsync(
            new HealthEvent(Guid.NewGuid(), second, new DateOnly(2026, 3, 4),
                HealthCategory.Diagnosis, "Severe aortic stenosis"), CancellationToken.None);
        await store.RecordAsync(
            new HealthEvent(Guid.NewGuid(), first, new DateOnly(2026, 1, 30),
                HealthCategory.Diagnosis, "Severe aortic stenosis"), CancellationToken.None);

        var timeline = await this.TimelineAsync(store);

        // When it became true, not when it was last mentioned.
        Assert.Equal(new DateOnly(2026, 1, 30), timeline[0].EventDate);
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
