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
        await log.RecordAsync(Guid.NewGuid(), "scout", Guid.NewGuid(), ranAt.AddDays(-1), ProactiveStatus.Completed, CancellationToken.None);
        await log.RecordAsync(Guid.NewGuid(), "scout", Guid.NewGuid(), ranAt, ProactiveStatus.Completed, CancellationToken.None);

        Assert.Equal(ranAt, await log.LastRanAtAsync("scout", CancellationToken.None));
    }

    [Fact]
    public async Task LastRanAtAsync_Should_Count_A_Failed_Run()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        await log.RecordAsync(Guid.NewGuid(), "scout", Guid.NewGuid(), ranAt, ProactiveStatus.Failed, CancellationToken.None);

        Assert.Equal(ranAt, await log.LastRanAtAsync("scout", CancellationToken.None));
    }

    [Fact]
    public async Task LastRanAtAsync_Should_Not_See_Another_Service()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        await log.RecordAsync(Guid.NewGuid(), "reflection", Guid.NewGuid(), ranAt, ProactiveStatus.Completed, CancellationToken.None);

        Assert.Null(await log.LastRanAtAsync("scout", CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_Should_Reject_A_Conflicting_Run_Id()
    {
        await this.fixture.ResetAsync();
        var log = this.CreateLog();
        var runId = Guid.NewGuid();
        await log.RecordAsync(
            runId, "scout", Guid.NewGuid(), ranAt, ProactiveStatus.Completed, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => log.RecordAsync(
            runId,
            "reflection",
            Guid.NewGuid(),
            ranAt.AddMinutes(1),
            ProactiveStatus.Failed,
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

        await log.RecordAsync(runId, "scout", traceId, ranAt, ProactiveStatus.Completed, CancellationToken.None);
        await log.RecordAsync(runId, "scout", traceId, ranAt, ProactiveStatus.Completed, CancellationToken.None);

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

    private PostgresProactiveRunLog CreateLog()
    {
        return new PostgresProactiveRunLog(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresProactiveRunLog>.Instance);
    }
}
