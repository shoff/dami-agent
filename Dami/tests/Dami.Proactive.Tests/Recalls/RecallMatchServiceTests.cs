using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Dami.Proactive.Recalls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Recalls;

public sealed class RecallMatchServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 30, 23, 45, 0, TimeSpan.Zero);

    private readonly IHealthEventStore health = Substitute.For<IHealthEventStore>();
    private readonly IDomainFactStore store = Substitute.For<IDomainFactStore>();
    private readonly List<DomainFact> written = [];

    public RecallMatchServiceTests()
    {
        this.store.RecordAsync(Arg.Do<DomainFact>(this.written.Add), Arg.Any<CancellationToken>())
            .Returns(true);
        this.health.TimelineAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(HealthAsync(
                Medication("Warfarin 5 mg daily"),
                Medication("Started metoprolol 25 mg")));
        this.store.TimelineAsync("recall", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(
                Recall("[drug Class I] Warfarin Sodium Tablets, 5 mg — Super potent (D-001-2026)"),
                Recall("[drug Class II] Ibuprofen 200 mg caplets — Label mixup (D-002-2026)")));
    }

    [Fact]
    public async Task Should_Surface_A_Recall_Naming_A_Medication_On_Record()
    {
        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        var surfacing = Assert.Single(result.Surfacings);
        Assert.Contains("warfarin", surfacing.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_Surface_A_Recall_Matching_A_Configured_Watch_Term()
    {
        this.health.TimelineAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(HealthAsync());
        this.store.TimelineAsync("recall", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(
                Recall("[device Class I] Model X mechanical aortic valve — leaflet fracture (Z-1-2026)")));

        var result = await this.Service("aortic valve").RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("aortic valve", Assert.Single(result.Surfacings).Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Stay_Quiet_When_No_Recall_Touches_Anything_Local()
    {
        this.store.TimelineAsync("recall", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(
                Recall("[drug Class I] Ibuprofen 200 mg caplets — Label mixup (D-002-2026)")));

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal((0, 0), (result.Surfacings.Count, this.written.Count));
    }

    [Fact]
    public async Task Should_Not_Resurface_A_Match_Already_On_Record()
    {
        var first = await this.Service().RunPassAsync(Context(), CancellationToken.None);
        this.store.TimelineAsync("recall", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(
                Recall("[drug Class I] Warfarin Sodium Tablets, 5 mg — Super potent (D-001-2026)"),
                this.written[0]));

        var second = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal((1, 0), (first.Surfacings.Count, second.Surfacings.Count));
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    private static HealthEvent Medication(string description) => new(
        Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 1),
        HealthCategory.Medication, description);

    private static DomainFact Recall(string description) => new(
        Guid.NewGuid(), "recall", new DateOnly(2026, 8, 15), "drug", description,
        "recall-collector", now);

    private static async IAsyncEnumerable<HealthEvent> HealthAsync(params HealthEvent[] events)
    {
        foreach (var healthEvent in events)
        {
            yield return healthEvent;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<DomainFact> FactsAsync(params DomainFact[] facts)
    {
        foreach (var fact in facts)
        {
            yield return fact;
        }

        await Task.CompletedTask;
    }

    private RecallMatchService Service(params string[] watchTerms)
    {
        var options = new RecallSentinelOptions();
        if (watchTerms.Length > 0)
        {
            options.WatchTerms.Clear();
            foreach (var term in watchTerms)
            {
                options.WatchTerms.Add(term);
            }
        }

        return new RecallMatchService(
            this.health, this.store, Options.Create(options), new FakeTimeProvider(now),
            NullLogger<RecallMatchService>.Instance);
    }
}
