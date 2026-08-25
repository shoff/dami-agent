using Dami.Contracts.TaskBoard;

namespace Dami.Core.BoardImport;

/// <summary>Where an import came from, recorded so a board can be traced to its source.</summary>
/// <param name="Revision">The commit the file was read at.</param>
/// <param name="FeatureRequest">What the board is for, in the file's own words.</param>
/// <param name="Plan">How the board was produced.</param>
public sealed record TodoImportSource(string Revision, string FeatureRequest, string Plan);

/// <summary>The state one task should end up in, alongside the draft that creates it.</summary>
/// <remarks>
/// Separate from <see cref="BoardTaskDraft"/> because the draft is a creation shape and
/// carries no status, owner, or claim. The board reaches those through guarded mutations,
/// so the import has to say what it wants and then ask for it one legal step at a time.
/// </remarks>
/// <param name="TaskId">The deterministic id the draft was created with.</param>
/// <param name="TodoId">The file's id for it, such as <c>G5a1</c>, when it had one.</param>
/// <param name="State">The state its marker denotes.</param>
/// <param name="Owner">Who claimed it in the file.</param>
/// <param name="ClaimedOn">When they claimed it.</param>
/// <param name="BlockedReason">The reason from a trailing BLOCKED annotation.</param>
/// <param name="Detail">A reason carried by the marker itself, such as DEFERRED's.</param>
/// <param name="CriterionIds">Its acceptance criteria, in order.</param>
/// <param name="Depth">How deep it sits, so children can be completed before parents.</param>
public sealed record DesiredTask(
    Guid TaskId,
    string? TodoId,
    TodoState State,
    string? Owner,
    DateOnly? ClaimedOn,
    string? BlockedReason,
    string? Detail,
    IReadOnlyList<Guid> CriterionIds,
    int Depth);

/// <summary>A board to create and the states its tasks should be moved to.</summary>
/// <param name="Draft">The board as it is first written.</param>
/// <param name="Desired">Every task's intended end state, deepest last.</param>
/// <param name="Anomalies">What the file could not say unambiguously.</param>
public sealed record TodoImportPlan(
    TaskBoardDraft Draft,
    IReadOnlyList<DesiredTask> Desired,
    IReadOnlyList<TodoAnomaly> Anomalies);
