using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Scheduling;
using Dami.Core.Reliability;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Reliability;

/// <summary>
/// What went wrong without anyone noticing. Users of every surveyed agent quit over silent
/// failures (2026-09-29); on that day Dami had three: stranded GPU sidecars for 40 hours, a
/// gate verdict cached for 30 minutes, and a speech sidecar that answered /health and
/// could not transcribe.
/// </summary>
public sealed class ReliabilityReportTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset since = now.AddDays(-1);

    private readonly IProactiveRunHistory history = Substitute.For<IProactiveRunHistory>();
    private readonly IScheduledJobStore jobs = Substitute.For<IScheduledJobStore>();
    private readonly IDisclosureLedger ledger = Substitute.For<IDisclosureLedger>();
    private readonly List<ISidecarProbe> probes = [];

    public ReliabilityReportTests()
    {
        this.Services();
        this.jobs.ListAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ScheduledJob>());
        this.ledger.RecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DisclosureDecision>());
    }

    [Fact]
    public async Task A_Quiet_Healthy_Day_Should_Have_No_Problems_And_Count_Its_Passes()
    {
        this.Services(Service("curator", ProactiveCadence.Nightly, now.AddHours(-3), Run(now.AddHours(-3))));

        var reading = await this.Subject().ReadAsync(since, CancellationToken.None);

        Assert.Empty(reading.Problems);
        Assert.Equal(1, reading.Passes);
        Assert.Equal(0, reading.FailedPasses);
    }

    [Fact]
    public async Task Failed_Passes_In_The_Window_Should_Be_Named()
    {
        this.Services(Service(
            "gallery-curator", ProactiveCadence.EightHourly, now.AddHours(-1),
            Run(now.AddHours(-1), ProactiveStatus.Failed), Run(now.AddHours(-9)), Run(now.AddDays(-3), ProactiveStatus.Failed)));

        var reading = await this.Subject().ReadAsync(since, CancellationToken.None);

        Assert.Equal(["gallery-curator failed 1 of 2 passes"], reading.Problems);
        Assert.Equal(1, reading.FailedPasses);
    }

    [Fact]
    public async Task A_Service_That_Stopped_Running_Should_Be_Named_Even_With_No_Failures()
    {
        // embedder, 2026-09-29: nightly, last pass 09-28 15:41, nothing failed — it just stopped.
        this.Services(Service("embedder", ProactiveCadence.Nightly, now.AddHours(-28), Run(now.AddHours(-28))));

        var reading = await this.Subject().ReadAsync(since, CancellationToken.None);

        Assert.Equal(["embedder (nightly) has not run for 28 hours"], reading.Problems);
    }

    [Fact]
    public async Task A_Job_That_Failed_Or_Missed_Its_Run_Should_Be_Named()
    {
        this.jobs.ListAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Job("portrait every six hours", now.AddHours(6), now.AddHours(-2), "Failed: Connection refused (127.0.0.1:8080)"),
            Job("morning weather", now.AddHours(-2), now.AddDays(-1), "Succeeded"),
            Job("fine", now.AddHours(3), now.AddHours(-1), "Succeeded"),
        ]);

        var reading = await this.Subject().ReadAsync(since, CancellationToken.None);

        Assert.Equal(
            ["job \"portrait every six hours\" failed: Connection refused (127.0.0.1:8080)",
             "job \"morning weather\" missed its run 2 hours ago"],
            reading.Problems);
    }

    [Fact]
    public async Task Turns_The_Gate_Could_Not_Judge_Should_Be_Counted()
    {
        var turn = Guid.NewGuid();
        this.ledger.RecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
        [
            Decision(turn, "gate output unreadable", now.AddHours(-5)),
            Decision(turn, "gate output unreadable", now.AddHours(-5)),
            Decision(Guid.NewGuid(), "not classified", now.AddHours(-2)),
            Decision(Guid.NewGuid(), "technical", now.AddHours(-2)),
            Decision(Guid.NewGuid(), "gate unavailable", now.AddDays(-3)),
        ]);

        var reading = await this.Subject().ReadAsync(since, CancellationToken.None);

        Assert.Equal(["the privacy gate could not judge 2 turn(s); their context was withheld"], reading.Problems);
    }

    [Fact]
    public async Task A_Sidecar_That_Fails_A_Real_Request_Should_Be_Named()
    {
        this.probes.Add(Probe("speech-to-text", new InvalidOperationException("CUDA failed: no CUDA-capable device")));
        this.probes.Add(Probe("embeddings", null));

        var reading = await this.Subject().ReadAsync(since, CancellationToken.None);

        Assert.Equal(["speech-to-text is not working: CUDA failed: no CUDA-capable device"], reading.Problems);
    }

    private ReliabilityReport Subject() =>
        new(this.history, this.jobs, this.ledger, this.probes, new FakeTimeProvider(now));

    private void Services(params ProactiveServiceHistory[] services) =>
        this.history.ReadAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(services);

    private static ProactiveServiceHistory Service(
        string name, ProactiveCadence cadence, DateTimeOffset last, params ProactiveRun[] runs) =>
        new(name, runs.Length, last, runs[0].Status, cadence, 0, 0, 0, runs);

    private static ProactiveRun Run(DateTimeOffset at, ProactiveStatus status = ProactiveStatus.Completed) =>
        new(Guid.NewGuid(), Guid.NewGuid(), at, status, 0, 0, 0, 1.0, 1);

    private static ScheduledJob Job(string name, DateTimeOffset next, DateTimeOffset last, string status) =>
        new(Guid.NewGuid(), name, "d", ScheduledJobKind.Prompt, "p", [], "0 * * * *", "UTC",
            ScheduledJobStatus.Active, now.AddDays(-9), now.AddDays(-9), next, last, status);

    private static DisclosureDecision Decision(Guid trace, string reason, DateTimeOffset at) =>
        new(Guid.NewGuid(), trace, "q", "line", Disclosure.Withhold, string.Empty, reason, at, null);

    private static ISidecarProbe Probe(string name, Exception? failure)
    {
        var probe = Substitute.For<ISidecarProbe>();
        probe.Name.Returns(name);
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(failure is null ? Task.CompletedTask : Task.FromException(failure));
        return probe;
    }
}
