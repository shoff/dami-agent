using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

/// <summary>Keeps the selected task and its version consistent with the displayed board.</summary>
public sealed partial class TaskBoardPanelState
{
    private IReadOnlyList<TaskBoardTaskNode> roots = [];
    private string query = string.Empty;

    /// <summary>The board whose replies may currently be displayed.</summary>
    public Guid? BoardId { get; private set; }

    /// <summary>Clears stale details immediately when the user changes boards.</summary>
    public void SelectBoard(Guid? boardId)
    {
        if (this.BoardId == boardId)
        {
            return;
        }

        this.BoardId = boardId;
        this.roots = [];
        this.Selected = null;
        this.Tasks.Clear();
        this.Activity.Clear();
        this.Title = "Select a plan";
        this.Detail = string.Empty;
        this.RefreshView();
    }

    /// <summary>Applies only a reply belonging to the current board.</summary>
    public void Present(TaskBoardSnapshot board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (board.BoardId != this.BoardId)
        {
            return;
        }

        this.Title = board.Title;
        this.Detail = board.Plan;
        this.roots = TaskBoardTaskNode.FromBoard(board.Tasks);
        this.RefreshView();
    }

    /// <summary>Changes the task view without a network round trip.</summary>
    public void Filter(BoardView view, string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        this.View = view;
        this.query = query;
        this.RefreshView();
    }

    /// <summary>Reconciles results and refreshes the selected version by stable id.</summary>
    public void RefreshView()
    {
        var selectedId = this.Selected?.TaskId;
        var results = BoardFilter.Search(this.roots, this.View, this.query);
        Reconcile.Sync(this.Tasks, results);
        this.Selected = this.Tasks.FirstOrDefault(task => task.TaskId == selectedId);
        this.NeedsYouCount = BoardFilter.Count(this.roots, BoardView.NeedsYou);
        this.OpenCount = BoardFilter.Count(this.roots, BoardView.Open);
        this.BlockedCount = BoardFilter.Count(this.roots, BoardView.Blocked);
        this.AllCount = BoardFilter.Count(this.roots, BoardView.All);
    }
}
