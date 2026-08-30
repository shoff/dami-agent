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

    /// <summary>A completed pass that did work worth recording but concluded nothing.</summary>
    /// <remarks>
    /// Collectors write into a domain store rather than returning conclusions or
    /// surfacings, so under the default label they read as "0 concluded, 0 surfaced" — a
    /// health pass that spent ten minutes extracting facts from a hundred and fifty notes
    /// looked identical to one that did nothing at all. This is how a pass says otherwise.
    /// </remarks>
    public static ProactiveResult Did(string note) =>
        new(Array.Empty<Conclusion>(), Array.Empty<Surfacing>(), ProactiveStatus.Completed, note);

    /// <summary>Creates a result.</summary>
    public ProactiveResult(
        IReadOnlyList<Conclusion> conclusions,
        IReadOnlyList<Surfacing> surfacings,
        ProactiveStatus status,
        string note = "")
    {
        ArgumentNullException.ThrowIfNull(conclusions);
        ArgumentNullException.ThrowIfNull(surfacings);
        ArgumentNullException.ThrowIfNull(note);

        this.Conclusions = conclusions;
        this.Surfacings = surfacings;
        this.Status = status;
        this.Note = note;
    }

    /// <summary>
    /// What the pass did, in its own terms, for the trace's completion line. Empty when
    /// the conclusion and surfacing counts already say everything worth saying.
    /// </summary>
    public string Note { get; }

    /// <summary>What the pass now believes. Written to the ledger, not shown to Steve.</summary>
    public IReadOnlyList<Conclusion> Conclusions { get; }

    /// <summary>What cleared the bar for Steve's attention. Usually empty.</summary>
    public IReadOnlyList<Surfacing> Surfacings { get; }

    /// <summary>How the pass ended.</summary>
    public ProactiveStatus Status { get; }
}
