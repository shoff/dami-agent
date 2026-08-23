using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests;

/// <summary>The pass runner: one pass in, ledger writes, queue writes, and events out.</summary>
public sealed class ProactivePassRunnerTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 2, 0, 0, TimeSpan.Zero);

    private readonly IExecutionEventStore eventStore = Substitute.For<IExecutionEventStore>();
    private readonly IConclusionLedger conclusionLedger = Substitute.For<IConclusionLedger>();
    private readonly ISurfacingQueue surfacingQueue = Substitute.For<ISurfacingQueue>();

    [Fact]
    public void Constructor_Should_Reject_A_Null_EventStore()
    {
        Assert.Throws<ArgumentNullException>(() => new ProactivePassRunner(
            null!, this.conclusionLedger, this.surfacingQueue,
            new FakeTimeProvider(now), NullLogger<ProactivePassRunner>.Instance));
    }

    [Fact]
    public async Task RunAsync_Should_Write_Every_Conclusion_To_The_Ledger()
    {
        var runner = this.CreateRunner();
        var service = ServiceReturning(Result(conclusions: 2));

        await runner.RunAsync(service, null, CancellationToken.None);

        await this.conclusionLedger.Received(2).RecordAsync(
            Arg.Any<Conclusion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Enqueue_Every_Surfacing()
    {
        var runner = this.CreateRunner();
        var service = ServiceReturning(Result(surfacings: 2));

        await runner.RunAsync(service, null, CancellationToken.None);

        await this.surfacingQueue.Received(2).EnqueueAsync(
            Arg.Any<Surfacing>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Emit_A_TraceStarted_With_ScheduledService_Origin()
    {
        var runner = this.CreateRunner();

        await runner.RunAsync(ServiceReturning(ProactiveResult.quiet), null, CancellationToken.None);

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item =>
                item.Type == ExecutionEventType.TraceStarted
                && item.Origin == ExecutionOrigin.ScheduledService),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Emit_A_TraceCompleted_For_A_Quiet_Pass()
    {
        var runner = this.CreateRunner();

        await runner.RunAsync(ServiceReturning(ProactiveResult.quiet), null, CancellationToken.None);

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.TraceCompleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Emit_A_Surfaced_Event_When_The_Queue_Accepts()
    {
        this.surfacingQueue.EnqueueAsync(Arg.Any<Surfacing>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var runner = this.CreateRunner();

        await runner.RunAsync(ServiceReturning(Result(surfacings: 1)), null, CancellationToken.None);

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.Surfaced),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Not_Emit_Surfaced_When_The_Cap_Suppressed_It()
    {
        this.surfacingQueue.EnqueueAsync(Arg.Any<Surfacing>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var runner = this.CreateRunner();

        await runner.RunAsync(ServiceReturning(Result(surfacings: 1)), null, CancellationToken.None);

        await this.eventStore.DidNotReceive().AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.Surfaced),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Report_Failed_When_The_Service_Throws()
    {
        var runner = this.CreateRunner();
        var service = Substitute.For<IProactiveService>();
        service.ServiceName.Returns("broken");
        service.RunPassAsync(Arg.Any<ProactiveContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProactiveResult>>(_ => throw new InvalidOperationException("boom"));

        var status = await runner.RunAsync(service, null, CancellationToken.None);

        Assert.Equal(ProactiveStatus.Failed, status);
    }

    [Fact]
    public async Task RunAsync_Should_Emit_TraceFailed_When_The_Service_Throws()
    {
        var runner = this.CreateRunner();
        var service = Substitute.For<IProactiveService>();
        service.ServiceName.Returns("broken");
        service.RunPassAsync(Arg.Any<ProactiveContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProactiveResult>>(_ => throw new InvalidOperationException("boom"));

        await runner.RunAsync(service, null, CancellationToken.None);

        await this.eventStore.Received(1).AppendAsync(
            Arg.Is<ExecutionEvent>(item => item.Type == ExecutionEventType.TraceFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Should_Pass_The_Last_Run_To_The_Service()
    {
        var runner = this.CreateRunner();
        var lastRan = now.AddDays(-1);
        ProactiveContext? seen = null;
        var service = Substitute.For<IProactiveService>();
        service.ServiceName.Returns("scout");
        service.RunPassAsync(
                Arg.Do<ProactiveContext>(context => seen = context),
                Arg.Any<CancellationToken>())
            .Returns(ProactiveResult.quiet);

        await runner.RunAsync(service, lastRan, CancellationToken.None);

        Assert.Equal(lastRan, seen!.LastRanAt);
    }

    private static IProactiveService ServiceReturning(ProactiveResult result)
    {
        var service = Substitute.For<IProactiveService>();
        service.ServiceName.Returns("scout");
        service.Cadence.Returns(ProactiveCadence.Nightly);
        service.RunPassAsync(Arg.Any<ProactiveContext>(), Arg.Any<CancellationToken>())
            .Returns(result);
        return service;
    }

    private static ProactiveResult Result(int conclusions = 0, int surfacings = 0)
    {
        var concluded = Enumerable.Range(0, conclusions)
            .Select(_ => new Conclusion(
                Guid.NewGuid(), null, "steve", "a belief", 0.6,
                ConclusionSource.ReflectionPass, now))
            .ToList();
        var surfaced = Enumerable.Range(0, surfacings)
            .Select(index => new Surfacing(
                Guid.NewGuid(), "scout", $"item {index}", "body", 0.8, now))
            .ToList();

        return new ProactiveResult(concluded, surfaced, ProactiveStatus.Completed);
    }

    private ProactivePassRunner CreateRunner()
    {
        return new ProactivePassRunner(
            this.eventStore,
            this.conclusionLedger,
            this.surfacingQueue,
            new FakeTimeProvider(now),
            NullLogger<ProactivePassRunner>.Instance);
    }
}
