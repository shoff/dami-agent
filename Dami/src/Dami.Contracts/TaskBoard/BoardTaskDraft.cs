namespace Dami.Contracts.TaskBoard;

/// <summary>One task in a new plan; children use this same recursive type.</summary>
public sealed record BoardTaskDraft
{
    /// <summary>Creates a task draft.</summary>
    public BoardTaskDraft(
        Guid taskId,
        string title,
        string description,
        TaskPriority priority,
        int position,
        TaskOrdering subTaskOrdering,
        IReadOnlyList<Guid> prerequisiteTaskIds,
        IReadOnlyList<AcceptanceCriterionDraft> acceptanceCriteria,
        IReadOnlyList<BoardTaskDraft> subTasks)
    {
        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("A task id cannot be empty.", nameof(taskId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ValidateEnums(priority, subTaskOrdering);
        this.TaskId = taskId;
        this.Title = title;
        this.Description = description;
        this.Priority = priority;
        this.Position = position;
        this.SubTaskOrdering = subTaskOrdering;
        this.PrerequisiteTaskIds = Copy(prerequisiteTaskIds);
        this.AcceptanceCriteria = Copy(acceptanceCriteria);
        this.SubTasks = Copy(subTasks);
    }

    /// <summary>Stable task identifier.</summary>
    public Guid TaskId { get; }

    /// <summary>Short task name.</summary>
    public string Title { get; }

    /// <summary>Scope and intent.</summary>
    public string Description { get; }

    /// <summary>Relative urgency.</summary>
    public TaskPriority Priority { get; }

    /// <summary>Stable sibling position.</summary>
    public int Position { get; }

    /// <summary>How this task's children are sorted.</summary>
    public TaskOrdering SubTaskOrdering { get; }

    /// <summary>Tasks that must be done first.</summary>
    public IReadOnlyList<Guid> PrerequisiteTaskIds { get; }

    /// <summary>Conditions required for completion.</summary>
    public IReadOnlyList<AcceptanceCriterionDraft> AcceptanceCriteria { get; }

    /// <summary>Zero or more tasks with the exact same structure.</summary>
    public IReadOnlyList<BoardTaskDraft> SubTasks { get; }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.ToArray();
    }

    private static void ValidateEnums(TaskPriority priority, TaskOrdering ordering)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");
        }

        if (!Enum.IsDefined(ordering))
        {
            throw new ArgumentOutOfRangeException(nameof(ordering), ordering, "Unknown ordering.");
        }
    }
}
