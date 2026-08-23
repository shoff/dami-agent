using Dami.Contracts.Memory;
using Dami.Persistence.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Memory;

/// <summary>The conclusions ledger against a live PostgreSQL instance.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresConclusionLedgerTests
{
    private static readonly DateTimeOffset concludedAt = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresConclusionLedgerTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public void Constructor_Should_Reject_A_Null_DataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresConclusionLedger(
            null!, Options.Create(new PostgresOptions()), NullLogger<PostgresConclusionLedger>.Instance));
    }

    [Fact]
    public async Task RecordAsync_Should_Make_The_Conclusion_Findable()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var conclusion = Believe("loses momentum on modelling projects around week six");

        await ledger.RecordAsync(conclusion, CancellationToken.None);

        Assert.NotNull(await ledger.FindAsync(conclusion.ConclusionId, CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_Should_Preserve_Supporting_Observations()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var observations = await this.SeedObservationsAsync(2);
        var conclusion = Believe("prefers evidence to assertion", observations);

        await ledger.RecordAsync(conclusion, CancellationToken.None);

        var found = await ledger.FindAsync(conclusion.ConclusionId, CancellationToken.None);
        Assert.Equal(observations.Count, found!.SupportingObservations.Count);
    }

    [Fact]
    public async Task ActiveForSubjectAsync_Should_Return_A_Recorded_Conclusion()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        await ledger.RecordAsync(Believe("reads the log before asking"), CancellationToken.None);

        Assert.Single(await this.ActiveAsync(ledger, "steve"));
    }

    [Fact]
    public async Task SupersedeAsync_Should_Leave_Only_The_Replacement_Active()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var original = Believe("dislikes being challenged");
        await ledger.RecordAsync(original, CancellationToken.None);

        var replacement = Believe("dislikes unevidenced challenge", supersedes: original.ConclusionId);
        await ledger.SupersedeAsync(replacement, "too broad", CancellationToken.None);

        Assert.Single(await this.ActiveAsync(ledger, "steve"));
    }

    [Fact]
    public async Task SupersedeAsync_Should_Retract_The_Conclusion_It_Replaces()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var original = Believe("dislikes being challenged");
        await ledger.RecordAsync(original, CancellationToken.None);

        var replacement = Believe("dislikes unevidenced challenge", supersedes: original.ConclusionId);
        await ledger.SupersedeAsync(replacement, "too broad", CancellationToken.None);

        var found = await ledger.FindAsync(original.ConclusionId, CancellationToken.None);
        Assert.False(found!.IsActive);
    }

    [Fact]
    public async Task SupersedeAsync_Should_Record_Why_The_Original_Was_Retracted()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var original = Believe("prefers terse answers");
        await ledger.RecordAsync(original, CancellationToken.None);

        var replacement = Believe("prefers terse answers with evidence", supersedes: original.ConclusionId);
        await ledger.SupersedeAsync(replacement, "incomplete", CancellationToken.None);

        var found = await ledger.FindAsync(original.ConclusionId, CancellationToken.None);
        Assert.Equal("incomplete", found!.RetractionReason);
    }

    [Fact]
    public async Task SupersedeAsync_Should_Reject_A_Replacement_That_Supersedes_Nothing()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();

        await Assert.ThrowsAsync<ArgumentException>(
            () => ledger.SupersedeAsync(Believe("unanchored"), "reason", CancellationToken.None));
    }

    [Fact]
    public async Task RetractAsync_Should_Remove_The_Conclusion_From_The_Active_Set()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var conclusion = Believe("wants a daily summary");
        await ledger.RecordAsync(conclusion, CancellationToken.None);

        await ledger.RetractAsync(conclusion.ConclusionId, "he said no", concludedAt, CancellationToken.None);

        Assert.Empty(await this.ActiveAsync(ledger, "steve"));
    }

    [Fact]
    public async Task FindAsync_Should_Return_Null_For_An_Unknown_Conclusion()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();

        Assert.Null(await ledger.FindAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static Conclusion Believe(
        string statement,
        IReadOnlyList<Guid>? observations = null,
        Guid? supersedes = null)
    {
        return new Conclusion(
            Guid.NewGuid(), supersedes, "steve", statement, 0.7,
            ConclusionSource.ReflectionPass, concludedAt, observations);
    }

    private async Task<List<Conclusion>> ActiveAsync(IConclusionLedger ledger, string subject)
    {
        var active = new List<Conclusion>();
        await foreach (var item in ledger.ActiveForSubjectAsync(subject, CancellationToken.None))
        {
            active.Add(item);
        }

        return active;
    }

    private async Task<IReadOnlyList<Guid>> SeedObservationsAsync(int count)
    {
        var ids = new List<Guid>();
        for (var index = 0; index < count; index++)
        {
            var id = Guid.NewGuid();
            await using var command = this.fixture.DataSource.CreateCommand(
                $"insert into {DatabaseFixture.SCHEMA}.observations (observation_id, occurred_at, source, body) "
                + "values (@id, @at, 'test', 'body')");
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("at", concludedAt);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            ids.Add(id);
        }

        return ids;
    }

    private PostgresConclusionLedger CreateLedger()
    {
        return new PostgresConclusionLedger(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresConclusionLedger>.Instance);
    }
}
