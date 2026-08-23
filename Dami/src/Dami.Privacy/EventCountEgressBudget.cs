using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Privacy;

/// <summary>The egress rate alarm: counts attempts in the event stream, trips loudly.</summary>
/// <remarks>
/// Both egress doors record an <c>EgressRequested</c> event before any gate runs, so the
/// stream itself is the counter — including refused attempts, which is what makes a
/// runaway loop visible while it is still being refused. On the transition from
/// within-budget to refused, one surfacing lands in the queue so the trip reaches Steve
/// rather than only the audit log; further refusals stay quiet until the budget
/// recovers. (Counting "exactly at the bound" was tried first and is wrong: concurrent
/// attempts jump the count straight past the bound and the alarm never fires.)
/// </remarks>
public sealed class EventCountEgressBudget : IEgressBudget
{
    private readonly IEgressMeter egressMeter;
    private readonly ISurfacingQueue surfacingQueue;
    private readonly EgressBudgetOptions budgetOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<EventCountEgressBudget> logger;
    private int tripped;

    /// <summary>Creates the budget.</summary>
    public EventCountEgressBudget(
        IEgressMeter egressMeter,
        ISurfacingQueue surfacingQueue,
        IOptions<EgressBudgetOptions> budgetOptions,
        TimeProvider clock,
        ILogger<EventCountEgressBudget> logger)
    {
        ArgumentNullException.ThrowIfNull(egressMeter);
        ArgumentNullException.ThrowIfNull(surfacingQueue);
        ArgumentNullException.ThrowIfNull(budgetOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.egressMeter = egressMeter;
        this.surfacingQueue = surfacingQueue;
        this.budgetOptions = budgetOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> FindRefusalAsync(CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();

        var refusal = await this.CheckWindowAsync(
            "hour", now.AddHours(-1), this.budgetOptions.MaxPerHour, cancellationToken)
            .ConfigureAwait(false)
            ?? await this.CheckWindowAsync(
                "day", now.AddDays(-1), this.budgetOptions.MaxPerDay, cancellationToken)
                .ConfigureAwait(false);

        if (refusal is null)
        {
            Interlocked.Exchange(ref this.tripped, 0);
            return null;
        }

        if (Interlocked.Exchange(ref this.tripped, 1) == 0)
        {
            await this.SurfaceTripAsync(refusal, cancellationToken).ConfigureAwait(false);
        }

        this.logger.LogWarning("Egress budget refusal: {Reason}", refusal);
        return refusal;
    }

    private async Task<string?> CheckWindowAsync(
        string window,
        DateTimeOffset since,
        int bound,
        CancellationToken cancellationToken)
    {
        var attempts = await this.egressMeter
            .CountRequestsSinceAsync(since, cancellationToken).ConfigureAwait(false);
        if (attempts < bound)
        {
            return null;
        }

        return $"Egress budget exhausted: {attempts} attempt(s) in the last {window} "
            + $"(bound {bound}). Something is calling out faster than anything sanctioned does.";
    }

    private Task SurfaceTripAsync(string reason, CancellationToken cancellationToken)
    {
        var surfacing = new Surfacing(
            Guid.NewGuid(), "egress-budget", "Egress budget tripped", reason,
            confidence: 1.0, this.clock.GetUtcNow());
        return this.surfacingQueue.EnqueueAsync(surfacing, cancellationToken);
    }
}
