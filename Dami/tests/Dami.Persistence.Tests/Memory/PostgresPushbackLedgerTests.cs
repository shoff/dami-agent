using Dami.Contracts.Memory;
using Dami.Persistence.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Memory;

/// <summary>The pushback ledger, D-011's decay instrument, against a live database.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresPushbackLedgerTests
{
    private static readonly DateTimeOffset quarterStart = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset quarterEnd = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset inWindow = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset beforeWindow = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresPushbackLedgerTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public void Constructor_Should_Reject_A_Null_DataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresPushbackLedger(
            null!, Options.Create(new PostgresOptions()), NullLogger<PostgresPushbackLedger>.Instance));
    }

    [Fact]
    public async Task RecordAsync_Should_Make_The_Challenge_Readable()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        await ledger.RecordAsync(Challenge(inWindow), CancellationToken.None);

        Assert.Single(await this.BetweenAsync(ledger));
    }

    [Fact]
    public async Task ResolveAsync_Should_Record_How_The_Challenge_Landed()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var pushback = Challenge(inWindow);
        await ledger.RecordAsync(pushback, CancellationToken.None);

        await ledger.ResolveAsync(pushback.PushbackId, PushbackOutcome.Accepted, "he reinstalled", CancellationToken.None);

        var found = await this.BetweenAsync(ledger);
        Assert.Equal(PushbackOutcome.Accepted, found[0].Outcome);
    }

    [Fact]
    public async Task ResolveAsync_Should_Record_The_Follow_Up_Note()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var pushback = Challenge(inWindow);
        await ledger.RecordAsync(pushback, CancellationToken.None);

        await ledger.ResolveAsync(pushback.PushbackId, PushbackOutcome.Rejected, "proceeded anyway", CancellationToken.None);

        var found = await this.BetweenAsync(ledger);
        Assert.Equal("proceeded anyway", found[0].FollowUpNote);
    }

    [Fact]
    public async Task ResolveAsync_Should_Reject_An_Unknown_Pushback()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var unknownId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() => ledger.ResolveAsync(
            unknownId,
            PushbackOutcome.Accepted,
            "not present",
            CancellationToken.None));

        Assert.Contains(unknownId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RateAsync_Should_Count_Every_Challenge_In_The_Window()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        await ledger.RecordAsync(Challenge(inWindow), CancellationToken.None);
        await ledger.RecordAsync(Challenge(inWindow), CancellationToken.None);

        var rate = await ledger.RateAsync(quarterStart, quarterEnd, CancellationToken.None);

        Assert.Equal(2, rate.Total);
    }

    [Fact]
    public async Task RateAsync_Should_Exclude_Challenges_Outside_The_Window()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        await ledger.RecordAsync(Challenge(beforeWindow), CancellationToken.None);

        var rate = await ledger.RateAsync(quarterStart, quarterEnd, CancellationToken.None);

        Assert.Equal(0, rate.Total);
    }

    [Fact]
    public async Task RateAsync_Should_Break_The_Total_Down_By_Outcome()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var accepted = Challenge(inWindow);
        await ledger.RecordAsync(accepted, CancellationToken.None);
        await ledger.RecordAsync(Challenge(inWindow), CancellationToken.None);
        await ledger.ResolveAsync(accepted.PushbackId, PushbackOutcome.Accepted, null, CancellationToken.None);

        var rate = await ledger.RateAsync(quarterStart, quarterEnd, CancellationToken.None);

        Assert.Equal((1, 1), (rate.Accepted, rate.Unresolved));
    }

    [Fact]
    public async Task BetweenAsync_Should_Return_Challenges_Oldest_First()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var later = inWindow.AddDays(10);
        await ledger.RecordAsync(Challenge(later), CancellationToken.None);
        await ledger.RecordAsync(Challenge(inWindow), CancellationToken.None);

        var found = await this.BetweenAsync(ledger);

        Assert.Equal(inWindow, found[0].OccurredAt);
    }

    private static PushbackRecord Challenge(DateTimeOffset occurredAt)
    {
        return new PushbackRecord(
            Guid.NewGuid(), Guid.NewGuid(),
            "the benchmark measures index recall, not the model",
            "that the eval result compares embedding models",
            PushbackOutcome.Unresolved, occurredAt);
    }

    private async Task<List<PushbackRecord>> BetweenAsync(IPushbackLedger ledger)
    {
        var found = new List<PushbackRecord>();
        await foreach (var item in ledger.BetweenAsync(quarterStart, quarterEnd, CancellationToken.None))
        {
            found.Add(item);
        }

        return found;
    }

    private PostgresPushbackLedger CreateLedger()
    {
        return new PostgresPushbackLedger(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresPushbackLedger>.Instance);
    }
}
