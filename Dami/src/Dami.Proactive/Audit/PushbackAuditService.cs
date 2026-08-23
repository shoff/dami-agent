using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;

namespace Dami.Proactive.Audit;

/// <summary>D-011's quarterly review: is the tuning loop eating the auditor?</summary>
/// <remarks>
/// A system optimising on reactions learns that challenge produces negative ones, and
/// given six months it agrees with everything, warmly. The drift is invisible as tone
/// and visible as a count, so this service counts. Every pass records a conclusion; it
/// surfaces only when the pushback rate falls materially against the previous quarter —
/// the one signal worth interrupting for, because it means the guard is failing.
/// </remarks>
public sealed class PushbackAuditService : IProactiveService
{
    private const double MATERIAL_DROP = 0.5;
    private static readonly TimeSpan quarter = TimeSpan.FromDays(91);

    private readonly IPushbackLedger pushbackLedger;
    private readonly TimeProvider clock;
    private readonly ILogger<PushbackAuditService> logger;

    /// <summary>Creates the service.</summary>
    public PushbackAuditService(
        IPushbackLedger pushbackLedger,
        TimeProvider clock,
        ILogger<PushbackAuditService> logger)
    {
        ArgumentNullException.ThrowIfNull(pushbackLedger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.pushbackLedger = pushbackLedger;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "pushback-audit";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Quarterly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = this.clock.GetUtcNow();
        var current = await this.pushbackLedger
            .RateAsync(now - quarter, now, cancellationToken).ConfigureAwait(false);
        var previous = await this.pushbackLedger
            .RateAsync(now - quarter - quarter, now - quarter, cancellationToken).ConfigureAwait(false);

        var conclusion = BuildConclusion(current, previous, now);
        var surfacings = BuildSurfacings(current, previous, now);

        this.logger.LogInformation(
            "Pushback audit: {Current} this quarter against {Previous} last", current.Total, previous.Total);

        return new ProactiveResult([conclusion], surfacings, ProactiveStatus.Completed);
    }

    private static Conclusion BuildConclusion(PushbackRate current, PushbackRate previous, DateTimeOffset now)
    {
        var statement =
            $"Pushback rate: {current.Total} challenges this quarter "
            + $"({current.Accepted} accepted, {current.Rejected} rejected, {current.Unresolved} unresolved) "
            + $"against {previous.Total} the quarter before.";

        // Full confidence: this is a count, not an inference.
        return new Conclusion(
            Guid.NewGuid(), null, "dami", statement, 1.0, ConclusionSource.SelfAudit, now);
    }

    private static IReadOnlyList<Surfacing> BuildSurfacings(
        PushbackRate current,
        PushbackRate previous,
        DateTimeOffset now)
    {
        // No baseline yet, or a healthy rate: conclude quietly. Scarcity is the design.
        if (previous.Total == 0 || current.Total >= previous.Total * MATERIAL_DROP)
        {
            return [];
        }

        return
        [
            new Surfacing(
                Guid.NewGuid(),
                "pushback-audit",
                "The auditor may be decaying",
                $"Dami challenged you {current.Total} time(s) this quarter, down from {previous.Total}. "
                + "D-011 says this drift is invisible from inside the conversation - which is why this "
                + "message exists. Worth reading the pushback ledger and asking whether the drop is real.",
                1.0,
                now),
        ];
    }
}
