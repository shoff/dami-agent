namespace Dami.Core.BoardImport;

/// <summary>The states TODO.md's protocol section defines, plus the one it does not.</summary>
/// <remarks>
/// Deliberately not <c>TaskBoardStatus</c>. The board has five statuses and the board is
/// right to; this file has six distinguishable states, and <c>NeedsSteve</c> and
/// <c>Deferred</c> both collapse into <c>Blocked</c> on the way in. Collapsing at parse
/// time would throw the distinction away before anyone could report it.
/// </remarks>
public enum TodoState
{
    /// <summary><c>[ ]</c> — open, unclaimed.</summary>
    Open,

    /// <summary><c>[~ OWNER DATE]</c> — claimed and in progress.</summary>
    InProgress,

    /// <summary><c>[x]</c> — done and demonstrated.</summary>
    Done,

    /// <summary><c>[STEVE]</c> — waiting on Steve's key or decision, not being worked.</summary>
    NeedsSteve,

    /// <summary><c>[DEFERRED: reason]</c> — undocumented in the protocol; reported, not guessed at.</summary>
    Deferred,
}

/// <summary>Something in the file that could not be read, or could only be read by guessing.</summary>
/// <param name="LineNumber">The 1-based line it was found on.</param>
/// <param name="Line">The line itself, so the report can be checked against the source.</param>
/// <param name="Reason">What could not be determined.</param>
public sealed record TodoAnomaly(int LineNumber, string Line, string Reason);

/// <summary>One checklist entry, and everything under it.</summary>
/// <param name="Id">The task id such as <c>G5a1</c>, or null when the line carries none.</param>
/// <param name="Title">The entry text with the marker, id, and annotations removed.</param>
/// <param name="RawText">The original line, kept so the import loses nothing the file said.</param>
/// <param name="State">The state its marker denotes.</param>
/// <param name="Owner">Who claimed it, from an in-progress marker.</param>
/// <param name="ClaimedOn">When they claimed it.</param>
/// <param name="BlockedReason">The reason from a trailing <c>[BLOCKED: …]</c> annotation.</param>
/// <param name="StateDetail">The reason carried by a marker that has one, such as DEFERRED.</param>
/// <param name="LineNumber">Where it came from.</param>
/// <param name="Position">Its 0-based order among its siblings.</param>
/// <param name="AcceptanceItems">Acceptance references such as "acceptance item 4".</param>
/// <param name="DependsOnIds">Prerequisite ids, resolved against ids that exist in the file.</param>
/// <param name="Children">Nested entries, the same type at any depth.</param>
public sealed record TodoEntry(
    string? Id,
    string Title,
    string RawText,
    TodoState State,
    string? Owner,
    DateOnly? ClaimedOn,
    string? BlockedReason,
    string? StateDetail,
    int LineNumber,
    int Position,
    IReadOnlyList<string> AcceptanceItems,
    IReadOnlyList<string> DependsOnIds,
    IReadOnlyList<TodoEntry> Children);

/// <summary>One lettered epic section, such as "G · Interactive runtime".</summary>
/// <param name="Key">The section letter.</param>
/// <param name="Title">The section heading without its letter.</param>
/// <param name="Position">Its 0-based order in the file.</param>
/// <param name="Entries">Its top-level entries.</param>
public sealed record TodoSection(
    string Key,
    string Title,
    int Position,
    IReadOnlyList<TodoEntry> Entries);

/// <summary>TODO.md, read.</summary>
/// <param name="Sections">The epic sections, in file order.</param>
/// <param name="Anomalies">What could not be read without guessing.</param>
public sealed record TodoDocument(
    IReadOnlyList<TodoSection> Sections,
    IReadOnlyList<TodoAnomaly> Anomalies);
