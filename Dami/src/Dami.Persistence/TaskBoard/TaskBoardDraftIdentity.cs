using Dami.Contracts.TaskBoard;

namespace Dami.Persistence.TaskBoard;

internal static class TaskBoardDraftIdentity
{
    public static bool Matches(TaskBoardDraft draft, TaskBoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!BoardMatches(draft, snapshot))
        {
            return false;
        }

        var draftTasks = FlattenDrafts(draft.Tasks);
        var savedTasks = FlattenTasks(snapshot.Tasks);
        if (draftTasks.Count != savedTasks.Count)
        {
            return false;
        }

        foreach (var pair in draftTasks)
        {
            if (!savedTasks.TryGetValue(pair.Key, out var saved)
                || !TaskMatches(pair.Value, saved))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BoardMatches(TaskBoardDraft draft, TaskBoardSnapshot snapshot)
    {
        return draft.BoardId == snapshot.BoardId
            && string.Equals(draft.Title, snapshot.Title, StringComparison.Ordinal)
            && string.Equals(
                draft.FeatureRequest, snapshot.FeatureRequest, StringComparison.Ordinal)
            && string.Equals(draft.Plan, snapshot.Plan, StringComparison.Ordinal)
            && draft.CreatedBy == snapshot.CreatedBy
            && SamePostgresInstant(draft.CreatedAt, snapshot.CreatedAt)
            && draft.RootOrdering == snapshot.RootOrdering
            && draft.PlanningContext == snapshot.PlanningContext;
    }

    private static bool TaskMatches(DraftNode draft, SavedNode saved)
    {
        return draft.ParentTaskId == saved.ParentTaskId
            && string.Equals(draft.Task.Title, saved.Task.Title, StringComparison.Ordinal)
            && string.Equals(
                draft.Task.Description, saved.Task.Description, StringComparison.Ordinal)
            && draft.Task.Priority == saved.Task.Priority
            && draft.Task.Position == saved.Task.Position
            && draft.Task.SubTaskOrdering == saved.Task.SubTaskOrdering
            && draft.Task.PrerequisiteTaskIds.ToHashSet()
                .SetEquals(saved.Task.PrerequisiteTaskIds)
            && CriteriaMatch(draft.Task.AcceptanceCriteria, saved.Task.AcceptanceCriteria);
    }

    private static bool CriteriaMatch(
        IReadOnlyList<AcceptanceCriterionDraft> drafts,
        IReadOnlyList<AcceptanceCriterion> saved)
    {
        if (drafts.Count != saved.Count)
        {
            return false;
        }

        var byId = saved.ToDictionary(criterion => criterion.CriterionId);
        return drafts.All(draft => byId.TryGetValue(draft.CriterionId, out var criterion)
            && string.Equals(draft.Description, criterion.Description, StringComparison.Ordinal)
            && draft.Position == criterion.Position);
    }

    private static Dictionary<Guid, DraftNode> FlattenDrafts(
        IReadOnlyList<BoardTaskDraft> roots)
    {
        var result = new Dictionary<Guid, DraftNode>();
        var pending = new Stack<DraftNode>(roots.Reverse().Select(task => new DraftNode(task, null)));
        while (pending.TryPop(out var node))
        {
            result.Add(node.Task.TaskId, node);
            for (var index = node.Task.SubTasks.Count - 1; index >= 0; index--)
            {
                pending.Push(new DraftNode(node.Task.SubTasks[index], node.Task.TaskId));
            }
        }

        return result;
    }

    private static Dictionary<Guid, SavedNode> FlattenTasks(IReadOnlyList<BoardTask> roots)
    {
        var result = new Dictionary<Guid, SavedNode>();
        var pending = new Stack<SavedNode>(roots.Reverse().Select(task => new SavedNode(task, null)));
        while (pending.TryPop(out var node))
        {
            result.Add(node.Task.TaskId, node);
            for (var index = node.Task.SubTasks.Count - 1; index >= 0; index--)
            {
                pending.Push(new SavedNode(node.Task.SubTasks[index], node.Task.TaskId));
            }
        }

        return result;
    }

    private static bool SamePostgresInstant(DateTimeOffset left, DateTimeOffset right)
    {
        return left.ToUniversalTime().Ticks / 10 == right.ToUniversalTime().Ticks / 10;
    }

    private sealed record DraftNode(BoardTaskDraft Task, Guid? ParentTaskId);

    private sealed record SavedNode(BoardTask Task, Guid? ParentTaskId);
}
