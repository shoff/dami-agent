namespace Dami.Contracts.TaskBoard;

/// <summary>A durable task-board mutation kind.</summary>
public enum TaskBoardActivityKind
{
    /// <summary>A feature request, plan, and initial tree were created.</summary>
    BoardCreated,

    /// <summary>A task was added to an existing board.</summary>
    TaskAdded,

    /// <summary>An actor atomically claimed an open task.</summary>
    TaskClaimed,

    /// <summary>An acceptance criterion gained evidence.</summary>
    CriterionSatisfied,

    /// <summary>Acceptance evidence was withdrawn.</summary>
    CriterionReopened,

    /// <summary>A task passed its completion gates.</summary>
    TaskCompleted,

    /// <summary>A task was blocked, reopened, or cancelled.</summary>
    TaskStatusChanged,
}

/// <summary>One append-only human or agent change to a board.</summary>
public sealed record TaskBoardActivity(
    long Sequence,
    Guid EventId,
    Guid BoardId,
    Guid? TaskId,
    Guid? CriterionId,
    TaskBoardActivityKind Kind,
    TaskActor Actor,
    DateTimeOffset OccurredAt,
    TaskBoardStatus? FromStatus,
    TaskBoardStatus? ToStatus,
    string? Detail);
