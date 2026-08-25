using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

/// <summary>One line in the conversation.</summary>
/// <remarks>
/// It raises change notifications because a streaming reply mutates in place: without
/// them the text binds once, at zero characters, and the answer never appears.
/// </remarks>
public sealed class Message : INotifyPropertyChanged
{
    /// <summary>Creates a message.</summary>
    public Message(string who, string body)
    {
        this.Who = who;
        this.body = body;
    }

    private string body;
    private string meta = string.Empty;

    /// <summary>Who said it — "you" or "dami".</summary>
    public string Who { get; }

    /// <summary>What was said. Grows while a turn streams.</summary>
    public string Body
    {
        get => this.body;
        set => this.Set(ref this.body, value);
    }

    /// <summary>Accounting shown under Dami's replies once the turn reports it.</summary>
    public string Meta
    {
        get => this.meta;
        set => this.Set(ref this.meta, value);
    }

    /// <summary>True when this is Steve's own line, for styling.</summary>
    public bool IsYou => this.Who == "you";

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>One event in the live execution graph, already positioned in its span tree.</summary>
public sealed class GraphRow
{
    /// <summary>Creates a row.</summary>
    public GraphRow(string time, string status, int depth, string type, string actor, string label)
    {
        this.Time = time;
        this.Status = status;
        this.Depth = depth;
        this.Type = type;
        this.Actor = actor;
        this.Label = label;
    }

    /// <summary>When it happened.</summary>
    public string Time { get; }

    /// <summary>Running, Succeeded, Failed — drives the colour.</summary>
    public string Status { get; }

    /// <summary>Depth in the span tree; a child sits under its parent.</summary>
    public int Depth { get; }

    /// <summary>Indentation derived from <see cref="Depth"/>.</summary>
    public Avalonia.Thickness Indent => new(this.Depth * 16, 0, 0, 0);

    /// <summary>The event type.</summary>
    public string Type { get; }

    /// <summary>Which component acted.</summary>
    public string Actor { get; }

    /// <summary>The human-readable label. Never invented — it is what was persisted.</summary>
    public string Label { get; }
}

/// <summary>One item awaiting Steve's decision, or one thing Dami believes.</summary>
public sealed class SidebarItem
{
    /// <summary>Creates an item.</summary>
    public SidebarItem(string id, string headline, string detail)
    {
        this.Id = id;
        this.Headline = headline;
        this.Detail = detail;
    }

    /// <summary>Short id, for acting on it.</summary>
    public string Id { get; }

    /// <summary>The line that matters.</summary>
    public string Headline { get; }

    /// <summary>Provenance or context.</summary>
    public string Detail { get; }
}

/// <summary>One criterion with the owning version needed for an optimistic write.</summary>
public sealed class TaskBoardCriterionNode
{
    internal TaskBoardCriterionNode(AcceptanceCriterion criterion, long expectedTaskVersion)
    {
        this.CriterionId = criterion.CriterionId;
        this.Description = criterion.Description;
        this.IsSatisfied = criterion.IsSatisfied;
        this.ExpectedTaskVersion = expectedTaskVersion;
    }

    /// <summary>Stable criterion id.</summary>
    public Guid CriterionId { get; }

    /// <summary>Testable completion condition.</summary>
    public string Description { get; }

    /// <summary>Current evidence state.</summary>
    public bool IsSatisfied { get; }

    /// <summary>Owning task version displayed with this criterion.</summary>
    public long ExpectedTaskVersion { get; }

    /// <summary>Action that changes the current evidence state.</summary>
    public string ActionLabel => this.IsSatisfied ? "reopen" : "satisfy";
}

/// <summary>A recursive task presentation node built only from the shared contract.</summary>
public sealed class TaskBoardTaskNode
{
    private TaskBoardTaskNode(BoardTask task)
    {
        this.TaskId = task.TaskId;
        this.Title = task.Title;
        this.Description = task.Description;
        this.Status = task.Status;
        this.Priority = task.Priority;
        this.Version = task.Version;
        this.ClaimedBy = task.Claim?.Actor.ActorId ?? string.Empty;
        this.Prerequisites = string.Join(", ", task.PrerequisiteTaskIds
            .Select(id => id.ToString("N")[..8]));
        this.Criteria = task.AcceptanceCriteria
            .Select(item => new TaskBoardCriterionNode(item, task.Version)).ToArray();
        this.SubTasks = task.SubTasks.Select(From).ToArray();
    }

    /// <summary>Maps one task and every descendant without changing ordering.</summary>
    public static TaskBoardTaskNode From(BoardTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new TaskBoardTaskNode(task);
    }

    /// <summary>Stable task id.</summary>
    public Guid TaskId { get; }

    /// <summary>Task title.</summary>
    public string Title { get; }

    /// <summary>Task scope.</summary>
    public string Description { get; }

    /// <summary>Durable task state.</summary>
    public TaskBoardStatus Status { get; }

    /// <summary>Display priority.</summary>
    public TaskPriority Priority { get; }

    /// <summary>Optimistic version.</summary>
    public long Version { get; }

    /// <summary>Current claimant, or empty.</summary>
    public string ClaimedBy { get; }

    /// <summary>Short prerequisite ids.</summary>
    public string Prerequisites { get; }

    /// <summary>Acceptance rows tied to this version.</summary>
    public IReadOnlyList<TaskBoardCriterionNode> Criteria { get; }

    /// <summary>Recursive children of this exact presentation type.</summary>
    public IReadOnlyList<TaskBoardTaskNode> SubTasks { get; }

    /// <summary>Whether the task may be claimed.</summary>
    public bool CanClaim => this.Status == TaskBoardStatus.Open;

    /// <summary>Whether completion and blocking controls apply.</summary>
    public bool CanWork => this.Status == TaskBoardStatus.InProgress;

    /// <summary>Whether the task may be reopened.</summary>
    public bool CanReopen => this.Status == TaskBoardStatus.Blocked;

    /// <summary>Whether the task may be cancelled through the general status route.</summary>
    public bool CanCancel => this.Status is TaskBoardStatus.Open or TaskBoardStatus.Blocked;
}

/// <summary>Observable state for the desktop task-board panel.</summary>
public sealed class TaskBoardPanelState : INotifyPropertyChanged
{
    private string title = "select a board";
    private string detail = string.Empty;
    private string message = "loading task boards…";

    /// <summary>Recent boards with derived progress.</summary>
    public ObservableCollection<TaskBoardSummary> Boards { get; } = [];

    /// <summary>Roots of the selected recursive tree.</summary>
    public ObservableCollection<TaskBoardTaskNode> Tasks { get; } = [];

    /// <summary>Recent durable activity, newest first.</summary>
    public ObservableCollection<TaskBoardActivity> Activity { get; } = [];

    /// <summary>Selected board heading.</summary>
    public string Title
    {
        get => this.title;
        set => this.Set(ref this.title, value);
    }

    /// <summary>Selected request, plan, status, and update time.</summary>
    public string Detail
    {
        get => this.detail;
        set => this.Set(ref this.detail, value);
    }

    /// <summary>Last refresh, conflict, or failure message.</summary>
    public string Message
    {
        get => this.message;
        set => this.Set(ref this.message, value);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Everything the window binds to.</summary>
public sealed class WindowState
{
    /// <summary>The conversation, oldest first.</summary>
    public ObservableCollection<Message> Messages { get; } = [];

    /// <summary>The live execution graph.</summary>
    public ObservableCollection<GraphRow> Graph { get; } = [];

    /// <summary>Pending surfacings and approvals.</summary>
    public ObservableCollection<SidebarItem> Attention { get; } = [];

    /// <summary>The active belief ledger.</summary>
    public ObservableCollection<SidebarItem> Beliefs { get; } = [];

    /// <summary>Live collaborative task-board state.</summary>
    public TaskBoardPanelState TaskBoards { get; } = new();
}
