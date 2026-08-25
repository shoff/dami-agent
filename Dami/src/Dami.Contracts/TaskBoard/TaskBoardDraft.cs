namespace Dami.Contracts.TaskBoard;

/// <summary>A feature request, its plan, and its complete initial task tree.</summary>
public sealed record TaskBoardDraft
{
    /// <summary>Creates a board draft.</summary>
    public TaskBoardDraft(
        Guid boardId,
        string title,
        string featureRequest,
        string plan,
        TaskActor createdBy,
        DateTimeOffset createdAt,
        TaskOrdering rootOrdering,
        IReadOnlyList<BoardTaskDraft> tasks,
        TaskBoardPlanningContext? planningContext = null)
    {
        if (boardId == Guid.Empty)
        {
            throw new ArgumentException("A board id cannot be empty.", nameof(boardId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        ArgumentNullException.ThrowIfNull(createdBy);
        ArgumentNullException.ThrowIfNull(tasks);
        if (!Enum.IsDefined(rootOrdering))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rootOrdering), rootOrdering, "Unknown root ordering.");
        }

        this.BoardId = boardId;
        this.Title = title;
        this.FeatureRequest = featureRequest;
        this.Plan = plan;
        this.CreatedBy = createdBy;
        this.CreatedAt = createdAt;
        this.RootOrdering = rootOrdering;
        this.Tasks = tasks.ToArray();
        this.PlanningContext = planningContext;
    }

    /// <summary>Stable board identifier.</summary>
    public Guid BoardId { get; }

    /// <summary>Short feature name.</summary>
    public string Title { get; }

    /// <summary>The request as received, before planning.</summary>
    public string FeatureRequest { get; }

    /// <summary>The generated implementation plan.</summary>
    public string Plan { get; }

    /// <summary>Human or agent that created the board.</summary>
    public TaskActor CreatedBy { get; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>How root tasks are sorted.</summary>
    public TaskOrdering RootOrdering { get; }

    /// <summary>Root tasks.</summary>
    public IReadOnlyList<BoardTaskDraft> Tasks { get; }

    /// <summary>How an agent produced the initial plan, or null for direct human boards.</summary>
    public TaskBoardPlanningContext? PlanningContext { get; }
}
