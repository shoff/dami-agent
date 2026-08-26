using Dami.Contracts.Privacy;
using Dami.Persistence.Privacy;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Dami.Persistence.Tests.Privacy;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresDisclosureLedgerTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
    private readonly DatabaseFixture fixture;

    public PostgresDisclosureLedgerTests(DatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Ledger_Should_Record_Decisions_Accept_One_Correction_And_Serve_Corrections_As_Examples()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var traceId = Guid.NewGuid();
        await ledger.RecordAsync(traceId, "what should I ask the surgeon?",
            [
                new DisclosedItem("Steve's surgeon is Dr Harrison", Disclosure.Pass, "Steve's surgeon is Dr Harrison", "public"),
                new DisclosedItem("severe aortic stenosis", Disclosure.Disguise, "a patient has severe aortic stenosis", "clinical"),
            ], at, CancellationToken.None);

        var recent = await ledger.RecentAsync(10, CancellationToken.None);
        var wrong = recent.Single(item => item.Original.Contains("Harrison", StringComparison.Ordinal));
        var corrected = await ledger.CorrectAsync(
            wrong.DecisionId, new DisclosureCorrection(Disclosure.Withhold, "doctors' names never leave", "steve", at.AddMinutes(1)),
            CancellationToken.None);
        var again = await ledger.CorrectAsync(
            wrong.DecisionId, new DisclosureCorrection(Disclosure.Pass, "changed my mind", "steve", at.AddMinutes(2)),
            CancellationToken.None);
        var examples = await ledger.CorrectionsAsync(10, CancellationToken.None);
        var reread = await ledger.RecentAsync(10, CancellationToken.None);

        Assert.Equal(2, recent.Count);
        Assert.All(recent, item => Assert.Null(item.Correction));
        Assert.True(corrected);
        Assert.False(again);
        var example = Assert.Single(examples);
        Assert.Equal((wrong.DecisionId, Disclosure.Pass, Disclosure.Withhold, "doctors' names never leave", "steve"),
            (example.DecisionId, example.Disclosure, example.Correction!.Corrected, example.Correction.Note, example.Correction.CorrectedBy));
        Assert.Equal(Disclosure.Withhold, reread.Single(item => item.DecisionId == wrong.DecisionId).Correction!.Corrected);
    }

    [Fact]
    public async Task Ledger_Should_Refuse_Rewriting_A_Decision_Or_A_Correction()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        await ledger.RecordAsync(Guid.NewGuid(), "q", [new DisclosedItem("x", Disclosure.Pass, "x", "r")], at, CancellationToken.None);
        var decision = (await ledger.RecentAsync(1, CancellationToken.None)).Single();
        await ledger.CorrectAsync(decision.DecisionId, new DisclosureCorrection(Disclosure.Withhold, "n", "steve", at), CancellationToken.None);
        var unknown = await ledger.CorrectAsync(
            Guid.NewGuid(), new DisclosureCorrection(Disclosure.Pass, "nothing", "steve", at), CancellationToken.None);
        await using var rewriteDecision = this.fixture.DataSource.CreateCommand(
            $"update {DatabaseFixture.SCHEMA}.disclosure_decisions set disclosure = 'Withhold';");
        await using var rewriteCorrection = this.fixture.DataSource.CreateCommand(
            $"delete from {DatabaseFixture.SCHEMA}.disclosure_corrections;");

        var first = await Assert.ThrowsAsync<PostgresException>(() => rewriteDecision.ExecuteNonQueryAsync(CancellationToken.None));
        var second = await Assert.ThrowsAsync<PostgresException>(() => rewriteCorrection.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.False(unknown);
        Assert.Equal("23001", first.SqlState);
        Assert.Equal("23001", second.SqlState);
    }

    private PostgresDisclosureLedger CreateLedger()
    {
        return new PostgresDisclosureLedger(
            this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }
}
