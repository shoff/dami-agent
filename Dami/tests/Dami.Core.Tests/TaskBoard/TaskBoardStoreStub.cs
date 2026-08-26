using Dami.Contracts.TaskBoard;

namespace Dami.Core.Tests.TaskBoard;

internal abstract class TaskBoardStoreStub : ITaskBoardStore
{
    public virtual Task CreateAsync(TaskBoardDraft draft, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual Task<TaskBoardSnapshot?> FindAsync(
        Guid boardId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<TaskBoardSnapshot?>(null);
    }

    public async IAsyncEnumerable<TaskBoardSummary> ListRecentAsync(
        int limit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public Task<bool> TryAddTaskAsync(
        Guid boardId,
        Guid? parentTaskId,
        BoardTaskDraft draft,
        TaskActor actor,
        DateTimeOffset addedAt,
        string? detail,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<bool> TryClaimAsync(
        Guid taskId,
        long expectedVersion,
        TaskActor actor,
        DateTimeOffset claimedAt,
        string? detail,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<bool> TrySetCriterionAsync(
        Guid criterionId,
        long expectedTaskVersion,
        bool isSatisfied,
        TaskActor actor,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<bool> TryCompleteAsync(
        Guid taskId,
        long expectedVersion,
        TaskActor actor,
        DateTimeOffset completedAt,
        string? detail,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<bool> TrySetStatusAsync(
        Guid taskId,
        long expectedVersion,
        TaskBoardStatus status,
        TaskActor actor,
        string detail,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public async IAsyncEnumerable<TaskBoardActivity> ActivityAsync(
        Guid boardId,
        int limit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
