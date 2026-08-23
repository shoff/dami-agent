namespace Dami.Contracts.Memory;

/// <summary>The versioned, supersedable record of what Dami believes about Steve.</summary>
public interface IConclusionLedger
{
    /// <summary>Records a new conclusion.</summary>
    Task RecordAsync(Conclusion conclusion, CancellationToken cancellationToken);

    /// <summary>
    /// Records <paramref name="replacement"/> and retracts what it supersedes, atomically.
    /// </summary>
    /// <remarks>
    /// One operation rather than two, deliberately. Charter §9.4 requires corrections to
    /// supersede rather than silently coexist, and a caller that could record the
    /// replacement without retracting the original would leave both active — which is
    /// exactly the failure the rule exists to prevent.
    /// </remarks>
    Task SupersedeAsync(Conclusion replacement, string reason, CancellationToken cancellationToken);

    /// <summary>Retracts a conclusion without replacing it.</summary>
    Task RetractAsync(
        Guid conclusionId,
        string reason,
        DateTimeOffset retractedAt,
        CancellationToken cancellationToken);

    /// <summary>The currently believed conclusions for a subject, newest first.</summary>
    /// <remarks>Only this set is ever embedded (D-009).</remarks>
    IAsyncEnumerable<Conclusion> ActiveForSubjectAsync(string subject, CancellationToken cancellationToken);

    /// <summary>Reads one conclusion, active or retracted.</summary>
    Task<Conclusion?> FindAsync(Guid conclusionId, CancellationToken cancellationToken);
}
