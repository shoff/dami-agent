namespace Dami.Contracts.TaskBoard;

/// <summary>The current owner of an in-progress task.</summary>
public sealed record TaskClaim(TaskActor Actor, DateTimeOffset ClaimedAt);

/// <summary>One persisted task; children recursively use this same type.</summary>
public sealed record BoardTask(
    Guid TaskId,
    string Title,
    string Description,
    TaskBoardStatus Status,
    TaskPriority Priority,
    int Position,
    TaskOrdering SubTaskOrdering,
    TaskClaim? Claim,
    long Version,
    IReadOnlyList<Guid> PrerequisiteTaskIds,
    IReadOnlyList<AcceptanceCriterion> AcceptanceCriteria,
    IReadOnlyList<BoardTask> SubTasks);

/// <summary>A consistent point-in-time view of a feature plan and its task tree.</summary>
public sealed record TaskBoardSnapshot(
    Guid BoardId,
    string Title,
    string FeatureRequest,
    string Plan,
    TaskActor CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    TaskBoardStatus Status,
    TaskOrdering RootOrdering,
    IReadOnlyList<BoardTask> Tasks,
    TaskBoardPlanningContext? PlanningContext = null);
