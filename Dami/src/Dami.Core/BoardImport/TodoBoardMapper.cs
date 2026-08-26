using System.Text;
using Dami.Contracts.TaskBoard;

namespace Dami.Core.BoardImport;

/// <summary>Translates a parsed TODO.md onto the board's contracts.</summary>
/// <remarks>
/// Sections become root tasks and every checklist entry becomes a subtask of the same
/// type at any depth, which is what ADR-0021 means by a recursive task.
///
/// Two things this deliberately does not invent. The file carries no priority, so every
/// task is Normal and siblings are Ordered — file order is the only ranking it states.
/// And no status is assigned to a section unless every one of its children is Done, which
/// is an entailment rather than a guess, and the same condition the store already requires
/// before it will accept a completion.
/// </remarks>
public static class TodoBoardMapper
{
    /// <summary>The board key, fixed so reruns address the same board.</summary>
    public const string BOARD_KEY = "dami-core-suite";

    /// <summary>The board's title.</summary>
    public const string BOARD_TITLE = "Dami Core suite";

    /// <summary>
    /// The id a task with this TODO id has on the suite board, so a task created on the
    /// board and one imported from the file are the same task. Null for any other board,
    /// whose tasks have no file identity to be stable against.
    /// </summary>
    public static Guid? StableTaskId(Guid boardId, string todoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(todoId);
        return boardId == BoardImportIds.Board(BOARD_KEY) ? BoardImportIds.Task(BOARD_KEY, todoId) : null;
    }

    /// <summary>Maps a parsed document onto a board draft and its intended task states.</summary>
    /// <param name="document">The parsed file.</param>
    /// <param name="source">Where it came from.</param>
    /// <param name="createdBy">The actor performing the import.</param>
    /// <param name="createdAt">When the import ran.</param>
    /// <returns>The board to write and the states to move its tasks to.</returns>
    public static TodoImportPlan Map(
        TodoDocument document,
        TodoImportSource source,
        TaskActor createdBy,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(createdBy);
        if (document.Sections.Count == 0)
        {
            throw new ArgumentException("The document defines no epic sections.", nameof(document));
        }

        var byTodoId = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var section in document.Sections)
        {
            Identify(section, section.Entries, byTodoId);
        }

        var desired = new List<DesiredTask>();
        var draft = new TaskBoardDraft(
            BoardImportIds.Board(BOARD_KEY),
            BOARD_TITLE,
            source.FeatureRequest,
            $"{source.Plan} (revision {source.Revision})",
            createdBy,
            createdAt,
            TaskOrdering.Ordered,
            [.. document.Sections.Select(section => Root(section, byTodoId, desired))]);

        return new TodoImportPlan(draft, desired, document.Anomalies);
    }

    /// <summary>Assigns every entry its id first, so prerequisites can point at real tasks.</summary>
    private static void Identify(
        TodoSection section,
        IReadOnlyList<TodoEntry> entries,
        Dictionary<string, Guid> byTodoId)
    {
        foreach (var entry in entries)
        {
            if (entry.Id is not null)
            {
                byTodoId[entry.Id] = BoardImportIds.Task(BOARD_KEY, entry.Id);
            }

            Identify(section, entry.Children, byTodoId);
        }
    }

    private static BoardTaskDraft Root(
        TodoSection section,
        Dictionary<string, Guid> byTodoId,
        List<DesiredTask> desired)
    {
        var taskId = BoardImportIds.Section(BOARD_KEY, section.Key);
        var subTasks = section.Entries
            .Select(entry => Task(section, entry, byTodoId, desired, 1))
            .ToList();

        desired.Add(new DesiredTask(
            taskId, section.Key, RollUp(section), null, null, null, null, [], 0));

        return new BoardTaskDraft(
            taskId,
            $"{section.Key} · {section.Title}",
            $"Epic {section.Key} of the Dami Core blueprint.",
            TaskPriority.Normal,
            section.Position,
            TaskOrdering.Ordered,
            [],
            [],
            subTasks);
    }

    /// <summary>
    /// An epic is done when every child is; otherwise it stays open and the children carry
    /// the detail. Nothing in the file states an epic's own status.
    /// </summary>
    private static TodoState RollUp(TodoSection section)
    {
        return section.Entries.Count > 0 && section.Entries.All(entry => entry.State == TodoState.Done)
            ? TodoState.Done
            : TodoState.Open;
    }

    private static BoardTaskDraft Task(
        TodoSection section,
        TodoEntry entry,
        Dictionary<string, Guid> byTodoId,
        List<DesiredTask> desired,
        int depth)
    {
        var taskId = entry.Id is not null
            ? byTodoId[entry.Id]
            : BoardImportIds.Derived(BOARD_KEY, section.Key, entry.Title);
        var criteria = entry.AcceptanceItems
            .Select((text, index) => new AcceptanceCriterionDraft(
                BoardImportIds.Criterion(taskId, index), text, index))
            .ToList();

        desired.Add(new DesiredTask(
            taskId,
            entry.Id,
            entry.State,
            entry.Owner,
            entry.ClaimedOn,
            entry.BlockedReason,
            entry.StateDetail,
            [.. criteria.Select(criterion => criterion.CriterionId)],
            depth));

        return new BoardTaskDraft(
            taskId,
            Title(entry),
            Describe(entry),
            TaskPriority.Normal,
            entry.Position,
            TaskOrdering.Ordered,
            [.. entry.DependsOnIds.Where(byTodoId.ContainsKey).Select(id => byTodoId[id]).Distinct()],
            criteria,
            [.. entry.Children.Select(child => Task(section, child, byTodoId, desired, depth + 1))]);
    }

    private static string Title(TodoEntry entry)
    {
        var title = entry.Id is null ? entry.Title : $"{entry.Id} {entry.Title}";
        return title.Length <= 200 ? title : title[..200];
    }

    /// <summary>Keeps the source line, so the board loses nothing the file said.</summary>
    private static string Describe(TodoEntry entry)
    {
        var description = new StringBuilder(entry.RawText);
        if (entry.BlockedReason is not null)
        {
            description.Append("\n\nBlocked: ").Append(entry.BlockedReason);
        }

        if (entry.StateDetail is not null)
        {
            description.Append("\n\nMarker detail: ").Append(entry.StateDetail);
        }

        if (entry.Owner is not null)
        {
            description.Append("\n\nClaimed in TODO.md by ").Append(entry.Owner)
                .Append(" on ").Append(entry.ClaimedOn?.ToString("yyyy-MM-dd") ?? "an unstated date");
        }

        return description.ToString();
    }
}
