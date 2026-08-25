using Dami.Contracts.TaskBoard;

namespace Dami.Persistence.TaskBoard;

internal sealed record TaskDraftNode(BoardTaskDraft Draft, Guid? ParentTaskId);

internal static class TaskDraftGraph
{
    private const int MAX_TASKS = 1024;
    private const int MAX_DEPTH = 64;

    internal static IReadOnlyList<TaskDraftNode> Flatten(IReadOnlyList<BoardTaskDraft> roots)
    {
        var nodes = new List<TaskDraftNode>();
        foreach (var root in roots)
        {
            Add(root, null, 1, nodes);
        }

        return nodes;
    }

    internal static void Validate(IReadOnlyList<TaskDraftNode> nodes)
    {
        var ids = nodes.Select(node => node.Draft.TaskId).ToHashSet();
        if (ids.Count != nodes.Count)
        {
            throw new ArgumentException("Every task id in a board must be unique.", nameof(nodes));
        }

        foreach (var node in nodes)
        {
            if (node.Draft.PrerequisiteTaskIds.Any(id => !ids.Contains(id)))
            {
                throw new ArgumentException(
                    $"Task '{node.Draft.TaskId}' has a prerequisite outside its board.", nameof(nodes));
            }
        }

        ValidateAcyclic(nodes);
    }

    private static void ValidateAcyclic(IReadOnlyList<TaskDraftNode> nodes)
    {
        var remaining = nodes.ToDictionary(
            node => node.Draft.TaskId,
            node => node.Draft.PrerequisiteTaskIds.Distinct().Count());
        var dependents = BuildDependents(nodes);
        var ready = new Queue<Guid>(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (ready.TryDequeue(out var completed))
        {
            visited++;
            foreach (var dependent in dependents.GetValueOrDefault(completed, []))
            {
                remaining[dependent]--;
                if (remaining[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        if (visited != nodes.Count)
        {
            throw new ArgumentException("Task prerequisites must form an acyclic graph.", nameof(nodes));
        }
    }

    private static Dictionary<Guid, List<Guid>> BuildDependents(
        IReadOnlyList<TaskDraftNode> nodes)
    {
        var dependents = new Dictionary<Guid, List<Guid>>();
        foreach (var node in nodes)
        {
            foreach (var prerequisite in node.Draft.PrerequisiteTaskIds.Distinct())
            {
                if (!dependents.TryGetValue(prerequisite, out var tasks))
                {
                    tasks = [];
                    dependents.Add(prerequisite, tasks);
                }

                tasks.Add(node.Draft.TaskId);
            }
        }

        return dependents;
    }

    private static void Add(
        BoardTaskDraft draft,
        Guid? parentTaskId,
        int depth,
        ICollection<TaskDraftNode> nodes)
    {
        if (depth > MAX_DEPTH)
        {
            throw new ArgumentException(
                $"A task board cannot exceed {MAX_DEPTH} task levels.", nameof(draft));
        }

        if (nodes.Count == MAX_TASKS)
        {
            throw new ArgumentException(
                $"A task board cannot exceed {MAX_TASKS} tasks.", nameof(draft));
        }

        nodes.Add(new TaskDraftNode(draft, parentTaskId));
        foreach (var child in draft.SubTasks)
        {
            Add(child, draft.TaskId, depth + 1, nodes);
        }
    }
}
