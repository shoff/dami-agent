using Dami.Contracts.Domains;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Recalls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Recalls;

public sealed class RecallCollectorServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 30, 23, 30, 0, TimeSpan.Zero);

    private const string DRUGS = """
        { "results": [
            { "classification": "Class I", "product_description": "Warfarin Sodium Tablets, 5 mg",
              "reason_for_recall": "Super potent", "recall_initiation_date": "20260815",
              "recalling_firm": "Example Pharma", "recall_number": "D-001-2026" },
            { "classification": "Class III", "product_description": "Mislabeled shampoo",
              "reason_for_recall": "Label", "recall_initiation_date": "20260816",
              "recalling_firm": "Example", "recall_number": "D-002-2026" } ] }
        """;

    private const string CPSC = """
        [ { "RecallDate": "2026-08-14T00:00:00",
            "Title": "Example Tool Co. Recalls Angle Grinders",
            "Description": "The guard can detach.",
            "URL": "https://www.cpsc.gov/Recalls/2026/example",
            "Products": [ { "Name": "9-inch angle grinder" } ] },
          { "RecallDate": "2026-08-15T00:00:00",
            "Title": "Cozy Co. Recalls Scented Candles",
            "Description": "Fire hazard.",
            "URL": "https://www.cpsc.gov/Recalls/2026/candles",
            "Products": [ { "Name": "scented candle" } ] } ]
        """;

    private readonly IEgressClient egress = Substitute.For<IEgressClient>();
    private readonly IDomainFactStore store = Substitute.For<IDomainFactStore>();
    private readonly List<DomainFact> written = [];

    public RecallCollectorServiceTests()
    {
        this.store.RecordAsync(Arg.Do<DomainFact>(this.written.Add), Arg.Any<CancellationToken>())
            .Returns(true);
        this.store.TimelineAsync("recall", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync());
        this.Answer("api.fda.gov", DRUGS);
        this.Answer("saferproducts.gov", CPSC);
    }

    [Fact]
    public async Task Should_Record_Serious_Fda_Recalls_And_Skip_Class_III()
    {
        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(
            (true, false),
            (Contains(this.written, "Warfarin"), Contains(this.written, "shampoo")));
    }

    [Fact]
    public async Task Should_Surface_A_Cpsc_Recall_Matching_A_Household_Term()
    {
        var result = await this.Service("angle grinder").RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains(result.Surfacings, surfacing =>
            surfacing.Title.Contains("Angle Grinders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_Not_Record_Cpsc_Recalls_That_Match_Nothing_Configured()
    {
        await this.Service("angle grinder").RunPassAsync(Context(), CancellationToken.None);

        Assert.False(Contains(this.written, "Candles"));
    }

    [Fact]
    public async Task Should_Not_Surface_Fda_Recalls_Itself()
    {
        // The FDA rows are for the local-only matcher, which is the half allowed to
        // read health data. This half never decides what touches Steve's medications.
        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task Should_Survive_A_Dead_Source_And_Read_The_Rest()
    {
        this.egress.SendAsync(
                Arg.Is<EgressRequest>(request => request.Destination.Host == "api.fda.gov"),
                Arg.Any<CancellationToken>())
            .Returns<EgressResponse>(_ => throw new EgressRefusedException("host not allowlisted"));

        var result = await this.Service("angle grinder").RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(
            (ProactiveStatus.Completed, true),
            (result.Status, Contains(this.written, "Angle Grinders")));
    }

    private static bool Contains(List<DomainFact> facts, string text)
    {
        foreach (var fact in facts)
        {
            if (fact.Description.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    private static async IAsyncEnumerable<DomainFact> FactsAsync(params DomainFact[] facts)
    {
        foreach (var fact in facts)
        {
            yield return fact;
        }

        await Task.CompletedTask;
    }

    private void Answer(string host, string body)
    {
        this.egress.SendAsync(
                Arg.Is<EgressRequest>(request => request.Destination.Host.Contains(host, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
            .Returns(new EgressResponse(200, body));
    }

    private RecallCollectorService Service(params string[] householdTerms)
    {
        var options = new RecallSentinelOptions();
        foreach (var term in householdTerms)
        {
            options.HouseholdTerms.Add(term);
        }

        return new RecallCollectorService(
            this.store, this.egress, Options.Create(options), new FakeTimeProvider(now),
            NullLogger<RecallCollectorService>.Instance);
    }

    [Fact]
    public async Task Should_Not_Read_The_Local_Halfs_Match_Rows()
    {
        // The matcher writes drug names drawn from the health record into this domain.
        // This half holds the egress client and must never load them — the split is the
        // whole privacy argument for splitting the service in two.
        this.store.TimelineAsync("recall", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(new DomainFact(
                Guid.NewGuid(), "recall", new DateOnly(2026, 8, 15), "match",
                "matches 'warfarin': [drug Class I] Warfarin Sodium Tablets", "recall-match", now)));
        this.Answer("api.fda.gov", DRUGS);
        this.Answer("saferproducts.gov", "[]");

        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        // If the match row had been loaded into the dedup set it would still not be
        // written; what proves the read is skipped is that the notice is recorded, since
        // an 800-row window filled with match rows is how the leak becomes a defect.
        Assert.True(Contains(this.written, "Warfarin"));
    }
}
