using Dami.Contracts.TaskBoard;

namespace Dami.Persistence.TaskBoard;

internal static class TaskTreeBuilder
{
    internal static IReadOnlyList<BoardTask> Build(
        IReadOnlyDictionary<Guid, TaskRow> rows,
        TaskOrdering rootOrdering)
    {
        return BuildChildren(rows, null, rootOrdering);
    }

    private static IReadOnlyList<BoardTask> BuildChildren(
        IReadOnlyDictionary<Guid, TaskRow> rows,
        Guid? parentTaskId,
        TaskOrdering ordering)
    {
        return Sort(rows.Values.Where(row => row.ParentTaskId == parentTaskId), ordering)
            .Select(row => row.ToTask(BuildChildren(rows, row.TaskId, row.SubTaskOrdering)))
            .ToArray();
    }

    private static IOrderedEnumerable<TaskRow> Sort(
        IEnumerable<TaskRow> rows,
        TaskOrdering ordering)
    {
        return ordering == TaskOrdering.Ordered
            ? rows.OrderBy(row => row.Position).ThenBy(row => row.TaskId)
            : rows.OrderByDescending(row => row.Priority)
                .ThenBy(row => row.Position)
                .ThenBy(row => row.TaskId);
    }
}
