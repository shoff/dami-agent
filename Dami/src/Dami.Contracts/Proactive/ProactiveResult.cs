using Dami.Contracts.Memory;

namespace Dami.Contracts.Proactive;

/// <summary>Everything one proactive pass produced.</summary>
/// <remarks>
/// From dami-core-system-architecture.md §9.2, verbatim in intent: Surfacing is separate
/// from Conclusion, and most passes should return conclusions and an empty surfacing
/// list. The asymmetry is the scarcity principle expressed in the type system.
/// </remarks>
public sealed record ProactiveResult
{
    /// <summary>A completed pass that concluded nothing and surfaced nothing — the common case.</summary>
    /// <remarks>camelCase because static readonly is camelCase at every accessibility (§1).</remarks>
    public static readonly ProactiveResult quiet = new(
        Array.Empty<Conclusion>(), Array.Empty<Surfacing>(), ProactiveStatus.Completed);

    /// <summary>Creates a result.</summary>
    public ProactiveResult(
        IReadOnlyList<Conclusion> conclusions,
        IReadOnlyList<Surfacing> surfacings,
        ProactiveStatus status)
    {
        ArgumentNullException.ThrowIfNull(conclusions);
        ArgumentNullException.ThrowIfNull(surfacings);

        this.Conclusions = conclusions;
        this.Surfacings = surfacings;
        this.Status = status;
    }

    /// <summary>What the pass now believes. Written to the ledger, not shown to Steve.</summary>
    public IReadOnlyList<Conclusion> Conclusions { get; }

    /// <summary>What cleared the bar for Steve's attention. Usually empty.</summary>
    public IReadOnlyList<Surfacing> Surfacings { get; }

    /// <summary>How the pass ended.</summary>
    public ProactiveStatus Status { get; }
}
