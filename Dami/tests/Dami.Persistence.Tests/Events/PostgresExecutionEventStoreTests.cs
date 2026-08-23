using Dami.Contracts.Events;
using Dami.Persistence.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Events;

/// <summary>The event store against a live PostgreSQL instance.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresExecutionEventStoreTests
{
    /// <summary>Fixed rather than ambient: DateTimeOffset.UtcNow is banned, and a
    /// constant keeps the assertions deterministic.</summary>
    private static readonly DateTimeOffset occurredAt = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresExecutionEventStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public void Constructor_Should_Reject_A_Null_DataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresExecutionEventStore(
            null!, Options.Create(new PostgresOptions()), NullLogger<PostgresExecutionEventStore>.Instance));
    }

    [Fact]
    public void Constructor_Should_Reject_Null_Options()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresExecutionEventStore(
            this.fixture.DataSource, null!, NullLogger<PostgresExecutionEventStore>.Instance));
    }

    [Fact]
    public void Constructor_Should_Reject_A_Null_Logger()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresExecutionEventStore(
            this.fixture.DataSource, Options.Create(new PostgresOptions()), null!));
    }

    [Fact]
    public async Task AppendAsync_Should_Assign_An_Increasing_Sequence()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var trace = Guid.NewGuid();

        var first = await store.AppendAsync(Event(trace, ExecutionEventType.TraceStarted), CancellationToken.None);
        var second = await store.AppendAsync(Event(trace, ExecutionEventType.TraceCompleted), CancellationToken.None);

        Assert.True(second > first, $"expected an increasing sequence, got {first} then {second}");
    }

    [Fact]
    public async Task AppendAsync_Should_Be_Idempotent_On_A_Repeated_Event_Id()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var duplicate = Event(Guid.NewGuid(), ExecutionEventType.ToolRequested);

        var first = await store.AppendAsync(duplicate, CancellationToken.None);
        var again = await store.AppendAsync(duplicate, CancellationToken.None);

        Assert.Equal(first, again);
    }

    [Fact]
    public async Task AppendAsync_Should_Not_Duplicate_A_Retried_Event()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var trace = Guid.NewGuid();
        var duplicate = Event(trace, ExecutionEventType.ToolRequested);

        await store.AppendAsync(duplicate, CancellationToken.None);
        await store.AppendAsync(duplicate, CancellationToken.None);

        Assert.Single(await this.ReplayAsync(store, trace));
    }

    [Fact]
    public async Task ReplayAsync_Should_Return_Only_The_Requested_Trace()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var wanted = Guid.NewGuid();
        await store.AppendAsync(Event(wanted, ExecutionEventType.TraceStarted), CancellationToken.None);
        await store.AppendAsync(Event(Guid.NewGuid(), ExecutionEventType.TraceStarted), CancellationToken.None);

        var replayed = await this.ReplayAsync(store, wanted);

        Assert.All(replayed, item => Assert.Equal(wanted, item.TraceId));
    }

    [Fact]
    public async Task ReplayAsync_Should_Preserve_Metadata()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var trace = Guid.NewGuid();
        var metadata = new Dictionary<string, string> { ["tool"] = "terminal", ["exit"] = "0" };
        await store.AppendAsync(
            Event(trace, ExecutionEventType.ToolCompleted) with { }, CancellationToken.None);
        await store.AppendAsync(
            new ExecutionEvent(Guid.NewGuid(), trace, Guid.NewGuid(), null, ExecutionOrigin.UserTurn,
                "runtime", ExecutionEventType.ToolCompleted, ExecutionStatus.Succeeded,
                occurredAt, "ran a command", null, metadata),
            CancellationToken.None);

        var replayed = await this.ReplayAsync(store, trace);

        Assert.Equal("terminal", replayed[^1].Metadata!["tool"]);
    }

    [Fact]
    public async Task ReplayAsync_Should_Return_Events_In_Sequence_Order()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var trace = Guid.NewGuid();
        foreach (var type in new[] { ExecutionEventType.TraceStarted, ExecutionEventType.ToolRequested, ExecutionEventType.TraceCompleted })
        {
            await store.AppendAsync(Event(trace, type), CancellationToken.None);
        }

        var replayed = await this.ReplayAsync(store, trace);

        Assert.Equal(replayed.Select(item => item.Sequence).Order(), replayed.Select(item => item.Sequence));
    }

    [Fact]
    public async Task ReadSinceAsync_Should_Respect_The_Limit()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var trace = Guid.NewGuid();
        for (var index = 0; index < 5; index++)
        {
            await store.AppendAsync(Event(trace, ExecutionEventType.AgentProgressed), CancellationToken.None);
        }

        var page = new List<ExecutionEvent>();
        await foreach (var item in store.ReadSinceAsync(0, 3, CancellationToken.None))
        {
            page.Add(item);
        }

        Assert.Equal(3, page.Count);
    }

    [Fact]
    public async Task ReadSinceAsync_Should_Reject_A_Non_Positive_Limit()
    {
        var store = this.CreateStore();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.ReadSinceAsync(0, 0, CancellationToken.None));
    }

    private static ExecutionEvent Event(Guid traceId, ExecutionEventType type)
    {
        return new ExecutionEvent(
            Guid.NewGuid(), traceId, Guid.NewGuid(), null, ExecutionOrigin.UserTurn,
            "runtime", type, ExecutionStatus.Succeeded, occurredAt, type.ToString());
    }

    private async Task<List<ExecutionEvent>> ReplayAsync(IExecutionEventStore store, Guid traceId)
    {
        var replayed = new List<ExecutionEvent>();
        await foreach (var item in store.ReplayAsync(traceId, CancellationToken.None))
        {
            replayed.Add(item);
        }

        return replayed;
    }

    private PostgresExecutionEventStore CreateStore()
    {
        return new PostgresExecutionEventStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresExecutionEventStore>.Instance);
    }
}
