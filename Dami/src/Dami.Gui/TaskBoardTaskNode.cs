using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

/// <summary>Readable task readiness and completion guidance.</summary>
public sealed partial class TaskBoardTaskNode
{
    /// <summary>Compact human-readable status.</summary>
    public string StatusLabel => this.Status switch
    {
        TaskBoardStatus.Open => "Not started",
        TaskBoardStatus.InProgress => "In progress",
        TaskBoardStatus.Done => "Completed",
        _ => this.Status.ToString(),
    };

    /// <summary>Ancestor context without imported Markdown markers.</summary>
    public string DisplayPath => this.ParentPath.Replace("**", string.Empty, StringComparison.Ordinal)
        .Replace("`", string.Empty, StringComparison.Ordinal);

    /// <summary>Ownership visible beside the status.</summary>
    public string OwnerLabel => string.IsNullOrEmpty(this.ClaimedBy) ? "Unassigned" : this.ClaimedBy;

    /// <summary>A compact requirement count.</summary>
    public string CriteriaLabel => this.Criteria.Count == 0 ? "No requirements yet"
        : $"{this.Criteria.Count - this.RemainingCriteria} of {this.Criteria.Count} verified";

    /// <summary>Readable list title without imported Markdown markers.</summary>
    public string DisplayTitle => this.Title.Replace("**", string.Empty, StringComparison.Ordinal)
        .Replace("`", string.Empty, StringComparison.Ordinal);

    /// <summary>Whether the task has related child rows to show.</summary>
    public bool HasSubTasks => this.SubTasks.Count > 0;

    /// <summary>Whether the task has prerequisite rows to show.</summary>
    public bool HasDependencies => this.Dependencies.Count > 0;

    /// <summary>Maps all tasks with the same dependency lookup.</summary>
    public static IReadOnlyList<TaskBoardTaskNode> FromBoard(IReadOnlyList<BoardTask> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var index = new Dictionary<Guid, BoardTask>();
        var pending = new Stack<BoardTask>(roots);
        while (pending.TryPop(out var task))
        {
            index[task.TaskId] = task;
            foreach (var child in task.SubTasks)
            {
                pending.Push(child);
            }
        }

        return roots.Select(task => new TaskBoardTaskNode(task, string.Empty, index)).ToArray();
    }

    /// <summary>Named prerequisites, including missing references.</summary>
    public IReadOnlyList<TaskBoardDependency> Dependencies { get; }

    /// <summary>Prerequisites must be Done, matching the runtime gate.</summary>
    public int RemainingDependencies => this.Dependencies.Count(item => item.Status != TaskBoardStatus.Done);

    /// <summary>Children must be completed or cancelled before this parent completes.</summary>
    public int RemainingChildren => this.SubTasks.Count(item => item.CanRunWork);

    /// <summary>The task's own unverified requirements.</summary>
    public int RemainingCriteria => this.Criteria.Count(item => !item.IsSatisfied);

    /// <summary>Whether the displayed task has satisfied its local completion gates.</summary>
    public bool CanComplete => this.CanWork && this.RemainingCriteria == 0
        && this.RemainingDependencies == 0 && this.RemainingChildren == 0;

    /// <summary>The next action implied by the displayed state.</summary>
    public string NextStep => this.Status switch
    {
        _ when this.CanRunWork && this.RemainingDependencies > 0 =>
            $"Waiting for {this.RemainingDependencies} prerequisite task(s) to finish.",
        TaskBoardStatus.Open => "Start this task to begin tracking work.",
        TaskBoardStatus.Blocked => "Resolve the blocker, then reopen this task.",
        TaskBoardStatus.Done => "This task is complete.",
        TaskBoardStatus.Cancelled => "This task was cancelled.",
        _ when this.RemainingChildren > 0 =>
            $"Finish {this.RemainingChildren} remaining subtask(s) before completing.",
        _ when this.RemainingCriteria > 0 =>
            $"Verify {this.RemainingCriteria} remaining requirement{(this.RemainingCriteria == 1 ? string.Empty : "s")} before completing.",
        _ when this.Criteria.Count == 0 => "No requirements recorded. Add one, or complete when the work is finished.",
        _ => "All requirements are verified. Ready to complete.",
    };
}
