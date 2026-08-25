using Dami.Contracts.TaskBoard;

namespace Dami.Persistence.TaskBoard;

internal static class TaskBoardStatusDeriver
{
    internal static TaskBoardStatus Derive(IReadOnlyList<BoardTask> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var statuses = FlattenStatuses(roots);
        if (statuses.Count == 0)
        {
            return TaskBoardStatus.Open;
        }

        if (statuses.All(status => status == TaskBoardStatus.Cancelled))
        {
            return TaskBoardStatus.Cancelled;
        }

        if (statuses.All(IsTerminal))
        {
            return TaskBoardStatus.Done;
        }

        if (statuses.Contains(TaskBoardStatus.InProgress))
        {
            return TaskBoardStatus.InProgress;
        }

        return statuses.Contains(TaskBoardStatus.Blocked)
            && !statuses.Contains(TaskBoardStatus.Open)
            ? TaskBoardStatus.Blocked
            : TaskBoardStatus.Open;
    }

    private static List<TaskBoardStatus> FlattenStatuses(IReadOnlyList<BoardTask> roots)
    {
        var statuses = new List<TaskBoardStatus>();
        var pending = new Stack<BoardTask>(roots.Reverse());
        while (pending.TryPop(out var task))
        {
            statuses.Add(task.Status);
            for (var index = task.SubTasks.Count - 1; index >= 0; index--)
            {
                pending.Push(task.SubTasks[index]);
            }
        }

        return statuses;
    }

    private static bool IsTerminal(TaskBoardStatus status)
    {
        return status is TaskBoardStatus.Done or TaskBoardStatus.Cancelled;
    }
}
