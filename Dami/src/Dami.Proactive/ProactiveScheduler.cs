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
    private static readonly TimeSpan leaseDuration = TimeSpan.FromHours(4);

    private static readonly IReadOnlyDictionary<ProactiveCadence, TimeSpan> intervals =
        new Dictionary<ProactiveCadence, TimeSpan>
        {
            [ProactiveCadence.Nightly] = TimeSpan.FromDays(1),
            [ProactiveCadence.Weekly] = TimeSpan.FromDays(7),
            [ProactiveCadence.Quarterly] = TimeSpan.FromDays(91),
            [ProactiveCadence.EightHourly] = TimeSpan.FromHours(8),
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

            if (await this.TryRunAsync(service, cancellationToken).ConfigureAwait(false))
            {
                ran++;
            }
        }

        return ran;
    }

    private async Task<bool> TryRunAsync(
        IProactiveService service,
        CancellationToken cancellationToken)
    {
        var lease = await this.runLog.TryAcquireLeaseAsync(
            service.ServiceName,
            this.clock.GetUtcNow(),
            leaseDuration,
            cancellationToken).ConfigureAwait(false);

        if (lease is null)
        {
            return false;
        }

        await using (lease.ConfigureAwait(false))
        {
            var lastRanAt = await this.runLog
                .LastRanAtAsync(service.ServiceName, cancellationToken).ConfigureAwait(false);

            if (!this.IsDue(service.Cadence, lastRanAt))
            {
                return false;
            }

            await this.RunOneAsync(service, lastRanAt, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// Runs one named service now, due or not — an operator's deliberate act, recorded
    /// like any other run. False when no service has that name.
    /// </summary>
    public async Task<bool> RunNowAsync(string serviceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        var service = this.services.FirstOrDefault(
            candidate => string.Equals(candidate.ServiceName, serviceName, StringComparison.Ordinal));
        if (service is null)
        {
            return false;
        }

        var lastRanAt = await this.runLog.LastRanAtAsync(service.ServiceName, cancellationToken).ConfigureAwait(false);
        await this.RunOneAsync(service, lastRanAt, cancellationToken).ConfigureAwait(false);
        return true;
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
            Guid.NewGuid(), service.ServiceName, outcome.TraceId, ranAt, outcome.Status,
            service.Cadence, cancellationToken)
            .ConfigureAwait(false);

        this.logger.LogInformation(
            "Proactive service {ServiceName} ran with status {Status}", service.ServiceName, outcome.Status);
    }
}
