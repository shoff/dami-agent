namespace Dami.Contracts.Proactive;

/// <summary>One scheduled observer: the common shape of the whole proactive tier.</summary>
/// <remarks>
/// observe → correlate → conclude → threshold → surface (architecture §6.1). The
/// contract holds the last three of those honest: conclusions and surfacings come back
/// separately, and the runner — not the service — writes them, so a service cannot
/// bypass the ledger, the cap, or the event stream.
///
/// Services propose; they do not act (D-020). Nothing in this contract can perform an
/// external side effect, and that absence is deliberate.
/// </remarks>
public interface IProactiveService
{
    /// <summary>Stable name, used for scheduling, the cap, and the event stream.</summary>
    string ServiceName { get; }

    /// <summary>How often it runs.</summary>
    ProactiveCadence Cadence { get; }

    /// <summary>Runs one pass.</summary>
    Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken);
}
