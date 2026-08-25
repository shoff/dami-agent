using Dami.Contracts.TaskBoard;

namespace Dami.Core.TaskBoard;

internal static class FeaturePlanMapper
{
    private const int MAX_TASKS = 1024;

    internal static TaskBoardDraft Map(
        FeaturePlanningRequest request,
        FeaturePlanProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ValidateProposal(proposal);
        var planned = Flatten(proposal.Tasks);
        var taskIds = planned.ToDictionary(
            task => task.Key,
            task => StablePlanningId.Create(request.RequestId, $"task:{task.Key}"),
            StringComparer.Ordinal);
        ValidatePrerequisites(planned, taskIds);
        var tasks = proposal.Tasks
            .Select(task => MapTask(request.RequestId, task, taskIds))
            .ToArray();
        return new TaskBoardDraft(
            request.RequestId, proposal.Title, request.FeatureRequest, proposal.Plan,
            request.RequestedBy, request.RequestedAt, proposal.RootOrdering, tasks,
            new TaskBoardPlanningContext(request.Planner, request.Privacy, request.Origin));
    }

    private static BoardTaskDraft MapTask(
        Guid boardId,
        PlannedTask task,
        IReadOnlyDictionary<string, Guid> taskIds)
    {
        var criteria = task.AcceptanceCriteria
            .Select((description, position) => new AcceptanceCriterionDraft(
                StablePlanningId.Create(boardId, $"criterion:{task.Key}:{position}"),
                description, position))
            .ToArray();
        var prerequisites = task.PrerequisiteKeys.Select(key => taskIds[key]).ToArray();
        var children = task.SubTasks.Select(child => MapTask(boardId, child, taskIds)).ToArray();
        return new BoardTaskDraft(
            taskIds[task.Key], task.Title, task.Description, task.Priority, task.Position,
            task.SubTaskOrdering, prerequisites, criteria, children);
    }

    private static IReadOnlyList<PlannedTask> Flatten(IReadOnlyList<PlannedTask> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var tasks = new List<PlannedTask>();
        foreach (var root in roots)
        {
            Add(root, tasks);
        }

        return tasks;
    }

    private static void Add(PlannedTask task, ICollection<PlannedTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(task.SubTasks);
        if (tasks.Count == MAX_TASKS)
        {
            throw new ArgumentException(
                $"A feature plan cannot exceed {MAX_TASKS} tasks.", nameof(task));
        }

        tasks.Add(task);
        foreach (var child in task.SubTasks)
        {
            Add(child, tasks);
        }
    }

    private static void ValidateProposal(FeaturePlanProposal proposal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal.Plan);
        ArgumentNullException.ThrowIfNull(proposal.Tasks);
        if (proposal.Tasks.Count == 0)
        {
            throw new ArgumentException("A feature plan must contain at least one task.",
                nameof(proposal));
        }

        if (!Enum.IsDefined(proposal.RootOrdering))
        {
            throw new ArgumentOutOfRangeException(
                nameof(proposal), proposal.RootOrdering, "Unknown root ordering.");
        }
    }

    private static void ValidatePrerequisites(
        IReadOnlyList<PlannedTask> tasks,
        IReadOnlyDictionary<string, Guid> taskIds)
    {
        foreach (var task in tasks)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(task.Key);
            ArgumentNullException.ThrowIfNull(task.PrerequisiteKeys);
            if (task.PrerequisiteKeys.Any(key => !taskIds.ContainsKey(key)))
            {
                throw new ArgumentException(
                    $"Planned task '{task.Key}' references an unknown prerequisite.",
                    nameof(tasks));
            }
        }
    }
}
