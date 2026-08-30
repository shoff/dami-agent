using Dami.Contracts.Events;
using Dami.Contracts.Proactive;
using Dami.Persistence.Events;
using Dami.Persistence.Proactive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Proactive;

/// <summary>The per-run tallies, against a live database.</summary>
/// <remarks>
/// These are what let the workers view answer "which pass went wrong" without opening
/// every one, so they are worth pinning against real SQL rather than a stub: the counting
/// happens in Postgres, and a filter that is subtly wrong there is invisible from C#.
/// </remarks>
[Collection(DatabaseCollection.NAME)]
public sealed class ProactiveRunTallyTests
{
    private static readonly DateTimeOffset ranAt = new(2026, 8, 27, 22, 47, 44, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public ProactiveRunTallyTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task ReadAsync_Should_Count_What_A_Pass_Produced_And_Reached_Out_To()
    {
        await this.fixture.ResetAsync();
        var trace = Guid.NewGuid();
        await this.RecordAsync(trace, "scout", ranAt);
        await this.EmitAsync(trace, ExecutionEventType.EgressRequested, "feed scan", ranAt);
        await this.EmitAsync(trace, ExecutionEventType.EgressCompleted, "hnrss.org answered 200", ranAt.AddSeconds(1));
        await this.EmitAsync(trace, ExecutionEventType.Surfaced, "an item", ranAt.AddSeconds(4));
        await this.EmitAsync(trace, ExecutionEventType.Surfaced, "another", ranAt.AddSeconds(4));

        var run = Assert.Single((await this.ReadAsync()).Single().Recent);

        Assert.Equal(2, run.Produced);
        Assert.Equal(2, run.Egress);
        Assert.Equal(4, run.Events);
        Assert.Equal(4, run.Seconds, 1);
    }

    [Fact]
    public async Task ReadAsync_Should_Flag_A_Pass_That_Was_Refused_Even_Though_It_Completed()
    {
        // The whole reason this exists. The scout's 429 is a Succeeded event on a Completed
        // run of a green service; no status anywhere in the system reports it.
        await this.fixture.ResetAsync();
        var trace = Guid.NewGuid();
        await this.RecordAsync(trace, "scout", ranAt);
        await this.EmitAsync(trace, ExecutionEventType.EgressCompleted, "hnrss.org answered 429", ranAt);

        var run = Assert.Single((await this.ReadAsync()).Single().Recent);

        Assert.Equal(1, run.Alerts);
        Assert.True(run.HasAlerts);
        Assert.Equal(ProactiveStatus.Completed, run.Status);
    }

    [Fact]
    public async Task ReadAsync_Should_Not_Flag_A_Successful_Answer()
    {
        await this.fixture.ResetAsync();
        var trace = Guid.NewGuid();
        await this.RecordAsync(trace, "scout", ranAt);
        await this.EmitAsync(trace, ExecutionEventType.EgressCompleted, "hnrss.org answered 204", ranAt);

        Assert.False(Assert.Single((await this.ReadAsync()).Single().Recent).HasAlerts);
    }

    [Fact]
    public async Task ReadAsync_Should_Total_A_Service_Across_Its_Whole_History()
    {
        // Totals answer "what has this thing done for me", which is not a question about
        // the recent slice — so they are summed over every run, not the returned ones.
        await this.fixture.ResetAsync();
        foreach (var day in Enumerable.Range(0, 3))
        {
            var trace = Guid.NewGuid();
            await this.RecordAsync(trace, "scout", ranAt.AddDays(-day));
            await this.EmitAsync(trace, ExecutionEventType.Surfaced, "item", ranAt.AddDays(-day));
        }

        var scout = (await this.ReadAsync(recent: 1)).Single();

        Assert.Single(scout.Recent);
        Assert.Equal(3, scout.Runs);
        Assert.Equal(3, scout.TotalProduced);
    }

    [Fact]
    public async Task ReadAsync_Should_Report_Zeroes_For_A_Run_With_No_Events()
    {
        // A run whose trace left nothing behind still happened; the join must not drop it.
        await this.fixture.ResetAsync();
        await this.RecordAsync(Guid.NewGuid(), "scout", ranAt);

        var run = Assert.Single((await this.ReadAsync()).Single().Recent);

        Assert.Equal((0, 0, 0, 0), (run.Produced, run.Egress, run.Alerts, run.Events));
        Assert.Equal(0, run.Seconds);
    }

    private async Task<IReadOnlyList<ProactiveServiceHistory>> ReadAsync(int recent = 10) =>
        await this.CreateLog().ReadAsync(recent, CancellationToken.None);

    private Task RecordAsync(Guid trace, string service, DateTimeOffset at) =>
        this.CreateLog().RecordAsync(
            Guid.NewGuid(), service, trace, at, ProactiveStatus.Completed,
            ProactiveCadence.Nightly, CancellationToken.None);

    private async Task EmitAsync(
        Guid trace,
        ExecutionEventType type,
        string label,
        DateTimeOffset at)
    {
        var store = new PostgresExecutionEventStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresExecutionEventStore>.Instance);
        await store.AppendAsync(
            new ExecutionEvent(
                Guid.NewGuid(), trace, Guid.NewGuid(), null, ExecutionOrigin.ScheduledService,
                "scout", type, ExecutionStatus.Succeeded, at, label),
            CancellationToken.None);
    }

    private PostgresProactiveRunLog CreateLog() =>
        new(this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresProactiveRunLog>.Instance);
}
