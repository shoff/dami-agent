using Dami.Contracts.Memory;
using Dami.Persistence.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Memory;

/// <summary>The observation corpus against a live PostgreSQL instance.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresObservationCorpusTests
{
    private static readonly DateTimeOffset windowStart = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset windowEnd = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset inWindow = new(2026, 8, 15, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset outsideWindow = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresObservationCorpusTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public void Constructor_Should_Reject_A_Null_DataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresObservationCorpus(
            null!, Options.Create(new PostgresOptions()), NullLogger<PostgresObservationCorpus>.Instance));
    }

    [Fact]
    public async Task RecordAsync_Should_Make_The_Observation_Findable()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        var observation = Observed("he reinstalled the driver stack");

        await corpus.RecordAsync(observation, CancellationToken.None);

        Assert.NotNull(await corpus.FindAsync(observation.ObservationId, CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_Should_Assign_A_RecordedAt()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        var observation = Observed("primed the fuselage halves");

        await corpus.RecordAsync(observation, CancellationToken.None);

        var found = await corpus.FindAsync(observation.ObservationId, CancellationToken.None);
        Assert.NotNull(found!.RecordedAt);
    }

    [Fact]
    public async Task RecordAsync_Should_Round_Trip_Metadata()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        var metadata = new Dictionary<string, string> { ["channel"] = "cli", ["exit"] = "0" };
        var observation = new Observation(Guid.NewGuid(), inWindow, "terminal", "ran a build", metadata);

        await corpus.RecordAsync(observation, CancellationToken.None);

        var found = await corpus.FindAsync(observation.ObservationId, CancellationToken.None);
        Assert.Equal("cli", found!.Metadata!["channel"]);
    }

    [Fact]
    public async Task RecordAsync_Should_Discard_A_Repeat_Of_The_Same_Observation()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        var observation = Observed("said the eval set does not exist yet");

        await corpus.RecordAsync(observation, CancellationToken.None);
        await corpus.RecordAsync(observation, CancellationToken.None);

        Assert.Single(await this.BetweenAsync(corpus));
    }

    [Fact]
    public async Task RecordAsync_Should_Not_Let_A_Repeat_Rewrite_History()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        var original = Observed("the original wording");
        await corpus.RecordAsync(original, CancellationToken.None);

        var tampered = new Observation(original.ObservationId, inWindow, "test", "a revised wording");
        await corpus.RecordAsync(tampered, CancellationToken.None);

        var found = await corpus.FindAsync(original.ObservationId, CancellationToken.None);
        Assert.Equal("the original wording", found!.Body);
    }

    [Fact]
    public async Task FindAsync_Should_Return_Null_For_An_Unknown_Observation()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();

        Assert.Null(await corpus.FindAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task BetweenAsync_Should_Exclude_Observations_Outside_The_Window()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        await corpus.RecordAsync(Observed("inside", inWindow), CancellationToken.None);
        await corpus.RecordAsync(Observed("outside", outsideWindow), CancellationToken.None);

        Assert.Single(await this.BetweenAsync(corpus));
    }

    [Fact]
    public async Task BetweenAsync_Should_See_An_EpochZero_Observation_Through_Its_Date_Repair()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        var epochZero = new Observation(
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, "hermes-migration", "written on 2026-08-15");
        await corpus.RecordAsync(epochZero, CancellationToken.None);
        await this.RepairAsync(epochZero.ObservationId, inWindow);

        var found = await this.BetweenAsync(corpus);

        Assert.Equal(inWindow, found.Single().OccurredAt);
    }

    private async Task RepairAsync(Guid observationId, DateTimeOffset repairedTo)
    {
        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            insert into {DatabaseFixture.SCHEMA}.observation_date_repairs
                (observation_id, repaired_occurred_at, method)
            values (@id, @at, 'body-iso');
            """);
        command.Parameters.AddWithValue("id", observationId);
        command.Parameters.AddWithValue("at", repairedTo);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task BetweenAsync_Should_Return_Observations_Oldest_First()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        await corpus.RecordAsync(Observed("later", inWindow.AddDays(5)), CancellationToken.None);
        await corpus.RecordAsync(Observed("earlier", inWindow), CancellationToken.None);

        var found = await this.BetweenAsync(corpus);
        Assert.Equal("earlier", found[0].Body);
    }

    [Fact]
    public async Task FromSourceAsync_Should_Return_Only_That_Source()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        await corpus.RecordAsync(new Observation(Guid.NewGuid(), inWindow, "discord", "a message"), CancellationToken.None);
        await corpus.RecordAsync(Observed("a terminal line"), CancellationToken.None);

        var found = await this.FromSourceAsync(corpus, "discord", 10);
        Assert.All(found, item => Assert.Equal("discord", item.Source));
    }

    [Fact]
    public async Task FromSourceAsync_Should_Respect_The_Limit()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        for (var index = 0; index < 4; index++)
        {
            await corpus.RecordAsync(Observed($"line {index}", inWindow.AddMinutes(index)), CancellationToken.None);
        }

        Assert.Equal(2, (await this.FromSourceAsync(corpus, "test", 2)).Count);
    }

    [Fact]
    public async Task FromSourceAsync_Should_Reject_A_Non_Positive_Limit()
    {
        var corpus = this.CreateCorpus();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => corpus.FromSourceAsync("test", 0, CancellationToken.None));
    }

    private static Observation Observed(string body, DateTimeOffset? occurredAt = null)
    {
        return new Observation(Guid.NewGuid(), occurredAt ?? inWindow, "test", body);
    }

    private async Task<List<Observation>> BetweenAsync(IObservationCorpus corpus)
    {
        var found = new List<Observation>();
        await foreach (var item in corpus.BetweenAsync(windowStart, windowEnd, CancellationToken.None))
        {
            found.Add(item);
        }

        return found;
    }

    private async Task<List<Observation>> FromSourceAsync(IObservationCorpus corpus, string source, int limit)
    {
        var found = new List<Observation>();
        await foreach (var item in corpus.FromSourceAsync(source, limit, CancellationToken.None))
        {
            found.Add(item);
        }

        return found;
    }

    private PostgresObservationCorpus CreateCorpus()
    {
        return new PostgresObservationCorpus(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresObservationCorpus>.Instance);
    }
}
