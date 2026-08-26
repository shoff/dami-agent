namespace Dami.Contracts.TaskBoard;

/// <summary>Durable feature plans and recursively structured task state.</summary>
public interface ITaskBoardStore
{
    /// <summary>Atomically creates a board and its entire initial task tree.</summary>
    Task CreateAsync(TaskBoardDraft draft, CancellationToken cancellationToken);

    /// <summary>
    /// Adds one task (and its drafted subtree) to an existing board, under
    /// <paramref name="parentTaskId"/> or at the root when it is null. False when the board
    /// or parent is unknown, the parent is finished, or the task id already exists.
    /// </summary>
    Task<bool> TryAddTaskAsync(
        Guid boardId,
        Guid? parentTaskId,
        BoardTaskDraft draft,
        TaskActor actor,
        DateTimeOffset addedAt,
        string? detail,
        CancellationToken cancellationToken);

    /// <summary>Reads one consistent board snapshot.</summary>
    Task<TaskBoardSnapshot?> FindAsync(Guid boardId, CancellationToken cancellationToken);

    /// <summary>Streams recently active boards with task-derived progress.</summary>
    IAsyncEnumerable<TaskBoardSummary> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Atomically claims an open task when its optimistic version still matches.</summary>
    /// <remarks><paramref name="detail"/> is recorded on the activity row when given; null or blank is omitted.</remarks>
    Task<bool> TryClaimAsync(
        Guid taskId,
        long expectedVersion,
        TaskActor actor,
        DateTimeOffset claimedAt,
        string? detail,
        CancellationToken cancellationToken);

    /// <summary>Sets one criterion result and advances its task's optimistic version atomically.</summary>
    Task<bool> TrySetCriterionAsync(
        Guid criterionId,
        long expectedTaskVersion,
        bool isSatisfied,
        TaskActor actor,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    /// <summary>Completes a claimed task only after its evidence and child work are complete.</summary>
    Task<bool> TryCompleteAsync(
        Guid taskId,
        long expectedVersion,
        TaskActor actor,
        DateTimeOffset completedAt,
        string? detail,
        CancellationToken cancellationToken);

    /// <summary>Blocks, reopens, or cancels a task without bypassing completion gates.</summary>
    Task<bool> TrySetStatusAsync(
        Guid taskId,
        long expectedVersion,
        TaskBoardStatus status,
        TaskActor actor,
        string detail,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    /// <summary>Streams a bounded oldest-first mutation history.</summary>
    IAsyncEnumerable<TaskBoardActivity> ActivityAsync(
        Guid boardId,
        int limit,
        CancellationToken cancellationToken);
}
