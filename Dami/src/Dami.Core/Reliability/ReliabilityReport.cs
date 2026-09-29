using System.Globalization;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Scheduling;

namespace Dami.Core.Reliability;

/// <summary>What went wrong in a window, as plain lines, and how much ran.</summary>
public sealed record ReliabilityReading(IReadOnlyList<string> Problems, int Passes, int FailedPasses);

/// <summary>Reads what went wrong without anyone noticing.</summary>
public interface IReliabilityReport
{
    /// <summary>Problems since <paramref name="since"/>, and the proactive passes counted in that window.</summary>
    Task<ReliabilityReading> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>
/// Failed and stalled proactive services, failed and missed jobs, turns the privacy gate
/// could not judge, and sidecars that fail real work.
/// </summary>
/// <remarks>
/// Users of every surveyed personal agent name silent failure as why they quit
/// (docs/agent-landscape-2026-09.md §3). On 2026-09-29 Dami had three at once, and each was
/// found by accident. The lines are operational — service names and error text, nothing from
/// the profile — so they may go to Discord without the disclosure gate (D-012).
/// </remarks>
public sealed class ReliabilityReport : IReliabilityReport
{
    private const int RECENT_PER_SERVICE = 60;
    private const int DECISIONS_READ = 2000;

    private readonly IProactiveRunHistory history;
    private readonly IScheduledJobStore jobs;
    private readonly IDisclosureLedger ledger;
    private readonly IEnumerable<ISidecarProbe> probes;
    private readonly TimeProvider clock;

    /// <summary>Creates the report.</summary>
    public ReliabilityReport(
        IProactiveRunHistory history,
        IScheduledJobStore jobs,
        IDisclosureLedger ledger,
        IEnumerable<ISidecarProbe> probes,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(clock);
        this.history = history;
        this.jobs = jobs;
        this.ledger = ledger;
        this.probes = probes;
        this.clock = clock;
    }

    /// <inheritdoc />
    public async Task<ReliabilityReading> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        var problems = new List<string>();
        var services = await this.history.ReadAsync(RECENT_PER_SERVICE, cancellationToken).ConfigureAwait(false);
        var runs = services.SelectMany(service => service.Recent.Where(run => run.RanAt >= since)).ToList();
        problems.AddRange(services.SelectMany(service => ServiceProblems(service, since, now)));
        problems.AddRange(JobProblems(await this.jobs.ListAsync(cancellationToken).ConfigureAwait(false), since, now));
        problems.AddRange(await this.GateProblemsAsync(since, cancellationToken).ConfigureAwait(false));
        problems.AddRange(await this.ProbeProblemsAsync(cancellationToken).ConfigureAwait(false));
        return new ReliabilityReading(problems, runs.Count, runs.Count(run => run.Status == ProactiveStatus.Failed));
    }

    private static IEnumerable<string> ServiceProblems(ProactiveServiceHistory service, DateTimeOffset since, DateTimeOffset now)
    {
        var window = service.Recent.Where(run => run.RanAt >= since).ToList();
        var failed = window.Count(run => run.Status == ProactiveStatus.Failed);
        if (failed > 0)
        {
            yield return $"{service.ServiceName} failed {failed} of {window.Count} passes";
        }

        if (service.NextDueAt is { } due && service.Cadence is { } cadence && now - due > Grace(cadence))
        {
            yield return $"{service.ServiceName} ({Name(cadence)}) has not run for {Span(now - service.LastRanAt)}";
        }
    }

    private static IEnumerable<string> JobProblems(IReadOnlyList<ScheduledJob> jobs, DateTimeOffset since, DateTimeOffset now)
    {
        foreach (var job in jobs.Where(job => job.Status == ScheduledJobStatus.Active))
        {
            if (job.LastRunAt >= since && job.LastRunStatus is { } status
                && status.StartsWith("Failed", StringComparison.Ordinal))
            {
                yield return $"job \"{job.Name}\" failed: {status["Failed".Length..].TrimStart(':', ' ')}";
            }

            if (job.NextRunAt is { } next && now - next > TimeSpan.FromMinutes(30))
            {
                yield return $"job \"{job.Name}\" missed its run {Span(now - next)} ago";
            }
        }
    }

    private async Task<IEnumerable<string>> GateProblemsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var decisions = await this.ledger.RecentAsync(DECISIONS_READ, cancellationToken).ConfigureAwait(false);
        var turns = decisions
            .Where(decision => decision.DecidedAt >= since && IsFallback(decision.Reason))
            .Select(decision => decision.TraceId)
            .Distinct()
            .Count();
        return turns == 0 ? [] : [$"the privacy gate could not judge {turns} turn(s); their context was withheld"];
    }

    private async Task<IEnumerable<string>> ProbeProblemsAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        foreach (var probe in this.probes)
        {
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(TimeSpan.FromMinutes(1));
                await probe.ProbeAsync(budget.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                problems.Add($"{probe.Name} is not working: {exception.Message}");
            }
        }

        return problems;
    }

    /// <summary>The gate's own fallbacks (see <c>LocalDisclosureGate</c>), not its verdicts.</summary>
    private static bool IsFallback(string reason) =>
        reason.StartsWith("gate ", StringComparison.Ordinal) || reason == "not classified";

    private static TimeSpan Grace(ProactiveCadence cadence) => cadence switch
    {
        ProactiveCadence.EightHourly => TimeSpan.FromHours(2),
        ProactiveCadence.Nightly => TimeSpan.FromHours(3),
        ProactiveCadence.Weekly => TimeSpan.FromHours(12),
        _ => TimeSpan.FromDays(3),
    };

    private static string Name(ProactiveCadence cadence) => cadence switch
    {
        ProactiveCadence.EightHourly => "eight-hourly",
        _ => cadence.ToString().ToLowerInvariant(),
    };

    private static string Span(TimeSpan span) => span.TotalHours < 48
        ? $"{(int)span.TotalHours} hours"
        : $"{((int)span.TotalDays).ToString(CultureInfo.InvariantCulture)} days";
}
