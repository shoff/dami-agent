using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

/// <summary>Which slice of a board the panel is showing.</summary>
public enum BoardView
{
    /// <summary>All unfinished work.</summary>
    Active,

    /// <summary>Work currently started.</summary>
    InProgress,

    /// <summary>Completed work.</summary>
    Done,

    /// <summary>Only what is waiting on Steve's decision.</summary>
    NeedsYou,

    /// <summary>Everything still open, whoever it belongs to.</summary>
    Open,

    /// <summary>Everything blocked.</summary>
    Blocked,

    /// <summary>The whole imported tree, nesting intact.</summary>
    All,
}

/// <summary>Reduces a board to the slice worth looking at.</summary>
/// <remarks>
/// The Dami Core suite is 212 tasks of which 170 are finished agent work. Opening on the
/// full tree means hunting for the handful of decisions that are actually Steve's, and
/// those are leaves several levels down — so every view except <see cref="BoardView.All"/>
/// flattens. Pure and static, so it tests without a window.
/// </remarks>
public static class BoardFilter
{
    /// <summary>The tasks a view should list.</summary>
    public static IReadOnlyList<TaskBoardTaskNode> Apply(
        IReadOnlyList<TaskBoardTaskNode> roots,
        BoardView view)
    {
        ArgumentNullException.ThrowIfNull(roots);

        if (view == BoardView.All)
        {
            return roots;
        }

        var found = new List<TaskBoardTaskNode>();
        foreach (var root in roots)
        {
            Walk(root, view, found);
        }

        return found;
    }

    /// <summary>Flat results, with search across task text and ancestor names.</summary>
    public static IReadOnlyList<TaskBoardTaskNode> Search(
        IReadOnlyList<TaskBoardTaskNode> roots, BoardView view, string query)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(query);
        var tasks = new List<TaskBoardTaskNode>();
        foreach (var root in roots)
        {
            Walk(root, view, tasks);
        }

        var term = query.Trim();
        return tasks.Where(task => Contains(task.Title, term)
            || Contains(task.Description, term) || Contains(task.ParentPath, term)
            || Contains(task.ClaimedBy, term) || Contains(task.TaskId.ToString("N"), term)
            || task.Criteria.Any(item => Contains(item.Description, term))).ToArray();
    }

    private static bool Contains(string text, string query) =>
        text.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>How many tasks a view covers, for the count beside its button.</summary>
    /// <remarks>
    /// Always counts tasks, never rows. <see cref="BoardView.All"/> returns the nested
    /// roots for display, but reporting its <c>Count</c> as the number of roots would put
    /// "All 15" beside "Open 20" — a smaller total than one of its own parts, which reads
    /// as a bug because it is one.
    /// </remarks>
    public static int Count(IReadOnlyList<TaskBoardTaskNode> roots, BoardView view)
    {
        ArgumentNullException.ThrowIfNull(roots);

        if (view != BoardView.All)
        {
            return Apply(roots, view).Count;
        }

        return roots.Sum(CountTasks);
    }

    private static int CountTasks(TaskBoardTaskNode task)
    {
        return 1 + task.SubTasks.Sum(CountTasks);
    }

    private static void Walk(TaskBoardTaskNode task, BoardView view, List<TaskBoardTaskNode> found)
    {
        if (Matches(task, view))
        {
            found.Add(task);
        }

        foreach (var child in task.SubTasks)
        {
            Walk(child, view, found);
        }
    }

    private static bool Matches(TaskBoardTaskNode task, BoardView view)
    {
        return view switch
        {
            BoardView.Active => task.CanRunWork,
            BoardView.InProgress => task.Status == TaskBoardStatus.InProgress,
            BoardView.Done => task.Status == TaskBoardStatus.Done,
            BoardView.NeedsYou => WantsSteve(task),
            BoardView.Open => task.Status == TaskBoardStatus.Open,
            BoardView.Blocked => task.Status == TaskBoardStatus.Blocked,
            _ => true,
        };
    }

    /// <remarks>
    /// The blueprint marks Steve's own decisions with a literal <c>[STEVE]</c> in the
    /// text, and the import carried that through into the description. Finished work is
    /// excluded: a settled question still carries the marker that raised it.
    /// </remarks>
    private static bool WantsSteve(TaskBoardTaskNode task)
    {
        return task.Status is not (TaskBoardStatus.Done or TaskBoardStatus.Cancelled)
            && task.Description.Contains("STEVE", StringComparison.Ordinal);
    }
}
