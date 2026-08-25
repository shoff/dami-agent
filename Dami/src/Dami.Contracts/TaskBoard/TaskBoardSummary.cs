namespace Dami.Contracts.TaskBoard;

/// <summary>A bounded board-list row with task-derived progress.</summary>
public sealed record TaskBoardSummary(
    Guid BoardId,
    string Title,
    TaskBoardStatus Status,
    DateTimeOffset UpdatedAt,
    int TotalTasks,
    int DoneTasks,
    int BlockedTasks);
