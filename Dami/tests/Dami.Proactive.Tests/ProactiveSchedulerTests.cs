using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests;

/// <summary>Due-ness: cadence against the durable run log.</summary>
public sealed class ProactiveSchedulerTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 2, 0, 0, TimeSpan.Zero);

    private readonly IProactiveRunLog runLog = Substitute.For<IProactiveRunLog>();
    private readonly IExecutionEventStore eventStore = Substitute.For<IExecutionEventStore>();

    public ProactiveSchedulerTests()
    {
        this.runLog.TryAcquireLeaseAsync(
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IProactiveRunLease>());
    }

    [Fact]
    public async Task RunDueAsync_Should_Run_A_Service_That_Has_Never_Run()
    {
        this.runLog.LastRanAtAsync("scout", Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);

        var ran = await this.CreateScheduler(Scout()).RunDueAsync(CancellationToken.None);

        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task RunDueAsync_Should_Skip_A_Service_Inside_Its_Cadence()
    {
        this.runLog.LastRanAtAsync("scout", Arg.Any<CancellationToken>())
            .Returns(now.AddHours(-6));

        var ran = await this.CreateScheduler(Scout()).RunDueAsync(CancellationToken.None);

        Assert.Equal(0, ran);
    }

    [Fact]
    public async Task RunDueAsync_Should_Run_A_Service_Whose_Cadence_Has_Elapsed()
    {
        this.runLog.LastRanAtAsync("scout", Arg.Any<CancellationToken>())
            .Returns(now.AddDays(-1).AddMinutes(-1));

        var ran = await this.CreateScheduler(Scout()).RunDueAsync(CancellationToken.None);

        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task RunDueAsync_Should_Record_The_Run_In_The_Log()
    {
        this.runLog.LastRanAtAsync("scout", Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);

        await this.CreateScheduler(Scout()).RunDueAsync(CancellationToken.None);

        await this.runLog.Received(1).RecordAsync(
            Arg.Any<Guid>(), "scout", Arg.Any<Guid>(), now,
            ProactiveStatus.Completed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDueAsync_Should_Record_The_Emitted_Trace_Id()
    {
        this.runLog.LastRanAtAsync("scout", Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        var emittedTraceId = Guid.Empty;
        this.eventStore.AppendAsync(
                Arg.Do<ExecutionEvent>(item =>
                {
                    if (item.Type == ExecutionEventType.TraceStarted)
                    {
                        emittedTraceId = item.TraceId;
                    }
                }),
                Arg.Any<CancellationToken>())
            .Returns(1);

        await this.CreateScheduler(Scout()).RunDueAsync(CancellationToken.None);

        await this.runLog.Received(1).RecordAsync(
            Arg.Any<Guid>(), "scout", emittedTraceId, now,
            ProactiveStatus.Completed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDueAsync_Should_Record_A_Failed_Run_So_It_Is_Not_Hammered()
    {
        this.runLog.LastRanAtAsync("broken", Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        var broken = Substitute.For<IProactiveService>();
        broken.ServiceName.Returns("broken");
        broken.Cadence.Returns(ProactiveCadence.Nightly);
        broken.RunPassAsync(Arg.Any<ProactiveContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProactiveResult>>(_ => throw new InvalidOperationException("boom"));

        await this.CreateScheduler(broken).RunDueAsync(CancellationToken.None);

        await this.runLog.Received(1).RecordAsync(
            Arg.Any<Guid>(), "broken", Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(),
            ProactiveStatus.Failed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDueAsync_Should_Keep_Running_After_One_Service_Fails()
    {
        this.runLog.LastRanAtAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        var broken = Substitute.For<IProactiveService>();
        broken.ServiceName.Returns("broken");
        broken.Cadence.Returns(ProactiveCadence.Nightly);
        broken.RunPassAsync(Arg.Any<ProactiveContext>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProactiveResult>>(_ => throw new InvalidOperationException("boom"));

        var ran = await this.CreateScheduler(broken, Scout()).RunDueAsync(CancellationToken.None);

        Assert.Equal(2, ran);
    }

    [Fact]
    public async Task RunDueAsync_Should_Not_Run_The_Same_Service_Concurrently()
    {
        this.runLog.LastRanAtAsync("scout", Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        var leaseGranted = 0;
        var lease = Substitute.For<IProactiveRunLease>();
        this.runLog.TryAcquireLeaseAsync(
                "scout",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.CompareExchange(ref leaseGranted, 1, 0) == 0
                ? lease
                : null);
        var scout = Scout();

        await Task.WhenAll(
            this.CreateScheduler(scout).RunDueAsync(CancellationToken.None),
            this.CreateScheduler(scout).RunDueAsync(CancellationToken.None));

        await scout.Received(1).RunPassAsync(
            Arg.Any<ProactiveContext>(), Arg.Any<CancellationToken>());
    }

    private static IProactiveService Scout()
    {
        var service = Substitute.For<IProactiveService>();
        service.ServiceName.Returns("scout");
        service.Cadence.Returns(ProactiveCadence.Nightly);
        service.RunPassAsync(Arg.Any<ProactiveContext>(), Arg.Any<CancellationToken>())
            .Returns(ProactiveResult.quiet);
        return service;
    }

    private ProactiveScheduler CreateScheduler(params IProactiveService[] services)
    {
        var runner = new ProactivePassRunner(
            this.eventStore,
            Substitute.For<IConclusionLedger>(),
            Substitute.For<ISurfacingQueue>(),
            new FakeTimeProvider(now),
            NullLogger<ProactivePassRunner>.Instance);

        return new ProactiveScheduler(
            services, runner, this.runLog, new FakeTimeProvider(now),
            NullLogger<ProactiveScheduler>.Instance);
    }
}
