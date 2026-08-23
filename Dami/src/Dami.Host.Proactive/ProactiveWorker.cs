using Dami.Contracts.Proactive;
using Dami.Proactive;

namespace Dami.Host.Proactive;

/// <summary>The tier's heartbeat: wakes hourly and runs whatever is due.</summary>
/// <remarks>
/// The loop is deliberately dumb — cadence intelligence lives in
/// <see cref="ProactiveScheduler"/> where it is tested, and the worker only decides how
/// often to ask. An hour is far finer than the coarsest-grained cadence, so a due pass
/// is never late by more than the tick.
/// </remarks>
public sealed class ProactiveWorker : BackgroundService
{
    private static readonly TimeSpan tick = TimeSpan.FromHours(1);

    private readonly ProactiveScheduler scheduler;
    private readonly IEnumerable<IProactiveService> services;
    private readonly TimeProvider clock;
    private readonly ILogger<ProactiveWorker> logger;

    /// <summary>Creates the worker.</summary>
    public ProactiveWorker(
        ProactiveScheduler scheduler,
        IEnumerable<IProactiveService> services,
        TimeProvider clock,
        ILogger<ProactiveWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.scheduler = scheduler;
        this.services = services;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.services.Any())
        {
            this.logger.LogWarning(
                "No IProactiveService is registered; the tier is idle. The interest scout is the designated first (D-019).");
        }

        using var timer = new PeriodicTimer(tick, this.clock);

        do
        {
            var ran = await this.scheduler.RunDueAsync(stoppingToken).ConfigureAwait(false);
            this.logger.LogInformation("Proactive tick: {Ran} pass(es) ran", ran);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
