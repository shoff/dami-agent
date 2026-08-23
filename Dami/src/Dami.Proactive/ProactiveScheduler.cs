using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;

namespace Dami.Proactive;

/// <summary>Decides which proactive services are due and runs them.</summary>
/// <remarks>
/// The tier's clock discipline lives here: a service runs when its cadence has elapsed
/// since its last recorded run, and a failure counts as a run — a broken service is
/// retried at its next cadence, never hammered in a loop. Due-ness is read from the
/// durable run log, so a restart neither re-runs everything nor forgets anything.
/// </remarks>
public sealed class ProactiveScheduler
{
    private static readonly IReadOnlyDictionary<ProactiveCadence, TimeSpan> intervals =
        new Dictionary<ProactiveCadence, TimeSpan>
        {
            [ProactiveCadence.Nightly] = TimeSpan.FromDays(1),
            [ProactiveCadence.Weekly] = TimeSpan.FromDays(7),
            [ProactiveCadence.Quarterly] = TimeSpan.FromDays(91),
        };

    private readonly IReadOnlyList<IProactiveService> services;
    private readonly ProactivePassRunner runner;
    private readonly IProactiveRunLog runLog;
    private readonly TimeProvider clock;
    private readonly ILogger<ProactiveScheduler> logger;

    /// <summary>Creates the scheduler.</summary>
    public ProactiveScheduler(
        IEnumerable<IProactiveService> services,
        ProactivePassRunner runner,
        IProactiveRunLog runLog,
        TimeProvider clock,
        ILogger<ProactiveScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(runLog);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.services = services.ToList();
        this.runner = runner;
        this.runLog = runLog;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Runs every service whose cadence has elapsed. Returns how many ran.</summary>
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var ran = 0;

        foreach (var service in this.services)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastRanAt = await this.runLog
                .LastRanAtAsync(service.ServiceName, cancellationToken).ConfigureAwait(false);

            if (!this.IsDue(service.Cadence, lastRanAt))
            {
                continue;
            }

            await this.RunOneAsync(service, lastRanAt, cancellationToken).ConfigureAwait(false);
            ran++;
        }

        return ran;
    }

    private bool IsDue(ProactiveCadence cadence, DateTimeOffset? lastRanAt)
    {
        if (lastRanAt is null)
        {
            return true;
        }

        return this.clock.GetUtcNow() - lastRanAt.Value >= intervals[cadence];
    }

    private async Task RunOneAsync(
        IProactiveService service,
        DateTimeOffset? lastRanAt,
        CancellationToken cancellationToken)
    {
        var ranAt = this.clock.GetUtcNow();
        var outcome = await this.runner
            .RunAsync(service, lastRanAt, cancellationToken).ConfigureAwait(false);

        await this.runLog.RecordAsync(
            Guid.NewGuid(), service.ServiceName, outcome.TraceId, ranAt, outcome.Status, cancellationToken)
            .ConfigureAwait(false);

        this.logger.LogInformation(
            "Proactive service {ServiceName} ran with status {Status}", service.ServiceName, outcome.Status);
    }
}
