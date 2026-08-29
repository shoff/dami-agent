using Dami.Contracts.Proactive;
using Dami.Persistence.Proactive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Proactive;

/// <summary>The run log against a live database.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresProactiveRunLogTests
{
    private static readonly DateTimeOffset ranAt = new(2026, 8, 22, 2, 30, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresProactiveRunLogTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public void Constructor_Should_Reject_A_Null_DataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresProactiveRunLog(
            null!, Options.Create(new PostgresOptions()), NullLogger<PostgresProactiveRunLog>.Instance));
    }

    [Fact]
    public async Task LastRanAtAsync_Should_Return_Null_For_A_Service_That_Never_Ran()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();

        Assert.Null(await log.LastRanAtAsync("scout", CancellationToken.None));
    }

    [Fact]
    public async Task LastRanAtAsync_Should_Return_The_Most_Recent_Run()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        await log.RecordAsync(Guid.NewGuid(), "scout", Guid.NewGuid(), ranAt.AddDays(-1), ProactiveStatus.Completed, ProactiveCadence.Nightly, CancellationToken.None);
        await log.RecordAsync(Guid.NewGuid(), "scout", Guid.NewGuid(), ranAt, ProactiveStatus.Completed, ProactiveCadence.Nightly, CancellationToken.None);

        Assert.Equal(ranAt, await log.LastRanAtAsync("scout", CancellationToken.None));
    }

    [Fact]
    public async Task LastRanAtAsync_Should_Count_A_Failed_Run()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        await log.RecordAsync(Guid.NewGuid(), "scout", Guid.NewGuid(), ranAt, ProactiveStatus.Failed, ProactiveCadence.Nightly, CancellationToken.None);

        Assert.Equal(ranAt, await log.LastRanAtAsync("scout", CancellationToken.None));
    }

    [Fact]
    public async Task LastRanAtAsync_Should_Not_See_Another_Service()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        await log.RecordAsync(Guid.NewGuid(), "reflection", Guid.NewGuid(), ranAt, ProactiveStatus.Completed, ProactiveCadence.Nightly, CancellationToken.None);

        Assert.Null(await log.LastRanAtAsync("scout", CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_Should_Reject_A_Conflicting_Run_Id()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        var runId = Guid.NewGuid();
        await log.RecordAsync(
            runId, "scout", Guid.NewGuid(), ranAt, ProactiveStatus.Completed, ProactiveCadence.Nightly, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => log.RecordAsync(
            runId,
            "reflection",
            Guid.NewGuid(),
            ranAt.AddMinutes(1),
            ProactiveStatus.Failed,
            ProactiveCadence.Weekly,
            CancellationToken.None));

        Assert.Contains(runId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordAsync_Should_Accept_An_Exact_Retry()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        var runId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        await log.RecordAsync(runId, "scout", traceId, ranAt, ProactiveStatus.Completed, ProactiveCadence.Nightly, CancellationToken.None);
        await log.RecordAsync(runId, "scout", traceId, ranAt, ProactiveStatus.Completed, ProactiveCadence.Nightly, CancellationToken.None);

        Assert.Equal(ranAt, await log.LastRanAtAsync("scout", CancellationToken.None));
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_Should_Grant_Only_One_Active_Lease()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();

        await using var first = await log.TryAcquireLeaseAsync(
            "scout", ranAt, TimeSpan.FromHours(1), CancellationToken.None);
        await using var second = await log.TryAcquireLeaseAsync(
            "scout", ranAt, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task ReadAsync_Should_Report_Each_Service_Newest_Active_First()
    {
        // The panel this feeds exists to answer "has that service run lately, and did it
        // work" without anyone opening psql.
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        await log.RecordAsync(
            Guid.NewGuid(), "scout", Guid.NewGuid(), ranAt.AddDays(-2), ProactiveStatus.Completed, ProactiveCadence.Nightly, CancellationToken.None);
        await log.RecordAsync(
            Guid.NewGuid(), "scout", Guid.NewGuid(), ranAt, ProactiveStatus.Failed, ProactiveCadence.Nightly, CancellationToken.None);
        await log.RecordAsync(
            Guid.NewGuid(), "curator", Guid.NewGuid(), ranAt.AddDays(-1), ProactiveStatus.Completed, ProactiveCadence.Nightly, CancellationToken.None);

        var history = await log.ReadAsync(10, CancellationToken.None);

        Assert.Equal(["scout", "curator"], history.Select(item => item.ServiceName));
        var scout = history[0];
        Assert.Equal(2, scout.Runs);
        Assert.Equal(ranAt, scout.LastRanAt);
        Assert.Equal(ProactiveStatus.Failed, scout.LastStatus);
        Assert.Equal([ranAt, ranAt.AddDays(-2)], scout.Recent.Select(run => run.RanAt));
    }

    [Fact]
    public async Task ReadAsync_Should_Bound_The_Runs_It_Returns_Per_Service()
    {
        // A service with months of history must not drag its whole log into a panel.
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        for (var day = 0; day < 6; day++)
        {
            await log.RecordAsync(
                Guid.NewGuid(), "scout", Guid.NewGuid(), ranAt.AddDays(-day),
                ProactiveStatus.Completed, ProactiveCadence.Nightly, CancellationToken.None);
        }

        var history = await log.ReadAsync(2, CancellationToken.None);

        var scout = Assert.Single(history);
        Assert.Equal(6, scout.Runs);
        Assert.Equal(2, scout.Recent.Count);
        Assert.Equal(ranAt, scout.Recent[0].RanAt);
    }

    [Fact]
    public async Task ReadAsync_Should_Return_Nothing_When_No_Service_Has_Run()
    {
        await this.fixture.ResetAsync();

        Assert.Empty(await this.CreateLog().ReadAsync(5, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadAsync_Should_Reject_A_Meaningless_Bound(int recent)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => this.CreateLog().ReadAsync(recent, CancellationToken.None));
    }

    private PostgresProactiveRunLog CreateLog()
    {
        return new PostgresProactiveRunLog(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresProactiveRunLog>.Instance);
    }
}
