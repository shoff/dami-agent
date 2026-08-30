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

/// <summary>What an attention row is, and therefore what can be done about it.</summary>
/// <remarks>
/// The panel used to be one undifferentiated list, which made it a scrolling log: every
/// row needed a trip to the CLI to act on, so none of them got acted on. A surfacing wants
/// a verdict, an approval wants a decision, and the rest is context that wants nothing.
/// </remarks>
public enum SidebarKind
{
    /// <summary>Context. Nothing to do about it here.</summary>
    Note,

    /// <summary>Something the proactive tier surfaced; a verdict trains the taste model.</summary>
    Surfacing,

    /// <summary>A consequential action waiting on a decision.</summary>
    Approval,
}

/// <summary>One item awaiting Steve's decision, or one thing Dami believes.</summary>
/// <remarks>
/// A record, deliberately: the sidebars rebuild these objects from JSON every two
/// seconds, and <see cref="Reconcile"/> can only tell "nothing changed" from "everything
/// changed" if equality is by value.
/// </remarks>
public sealed record SidebarItem
{
    /// <summary>Creates an item.</summary>
    public SidebarItem(
        string id,
        string headline,
        string detail,
        SidebarKind kind = SidebarKind.Note,
        string body = "")
    {
        this.Id = id;
        this.Headline = headline;
        this.Detail = detail;
        this.Kind = kind;
        this.Body = body;
    }

    /// <summary>
    /// What the item actually is — the link for a scouted item, the prose for anything
    /// else. Dropping this was why the panel could not be acted on: a verdict on a
    /// headline is a verdict on nothing.
    /// </summary>
    public string Body { get; }

    /// <summary>What this is, and therefore what can be done about it.</summary>
    public SidebarKind Kind { get; }

    /// <summary>Whether a verdict trains the taste model on this row.</summary>
    public bool CanRate => this.Kind == SidebarKind.Surfacing;

    /// <summary>Whether this row is a decision waiting on Steve.</summary>
    public bool CanApprove => this.Kind == SidebarKind.Approval;

    /// <summary>The link, when the body is one.</summary>
    public string? Link =>
        Uri.TryCreate(this.Body, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
            ? this.Body
            : null;

    /// <summary>Whether there is something to open.</summary>
    public bool CanOpen => this.Link is not null;

    /// <summary>
    /// The host, which is most of the signal a link carries before you read it: a
    /// personal blog and a GitHub repo want different scepticism.
    /// </summary>
    public string Source =>
        Uri.TryCreate(this.Body, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

    /// <summary>The body shown inline — the prose, or the link's path.</summary>
    public string Preview => this.Link is null ? this.Body : this.Body;

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
public sealed class TaskBoardTaskNode : IEquatable<TaskBoardTaskNode>
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

    /// <summary>
    /// Whether an advisory run can be asked for. Anything unfinished qualifies —
    /// including Blocked, where "what is actually blocking this" is often the useful
    /// question. Distinct from <see cref="CanWork"/>, which means "is InProgress".
    /// </summary>
    public bool CanRunWork =>
        this.Status is not (TaskBoardStatus.Done or TaskBoardStatus.Cancelled);

    /// <summary>
    /// Value equality over the parts that can actually change, so a re-poll that returned
    /// the same board does not rebuild the tree and collapse every expander.
    /// </summary>
    public bool Equals(TaskBoardTaskNode? other)
    {
        return other is not null
            && this.TaskId == other.TaskId
            && this.Version == other.Version
            && this.Status == other.Status
            && this.SubTasks.SequenceEqual(other.SubTasks);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => this.Equals(obj as TaskBoardTaskNode);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(this.TaskId, this.Version, this.Status);
}

/// <summary>Observable state for the desktop task-board panel.</summary>
public sealed class TaskBoardPanelState : INotifyPropertyChanged
{
    private string title = "select a board";
    private string detail = string.Empty;
    private string message = "loading task boards…";
    private TaskBoardTaskNode? selected;
    private bool hasSelection;
    private BoardView view = BoardView.NeedsYou;
    private int needsYouCount;
    private int openCount;
    private int blockedCount;
    private int allCount;

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

    /// <summary>The task the action bar acts on, or null when nothing is selected.</summary>
    public TaskBoardTaskNode? Selected
    {
        get => this.selected;
        set
        {
            this.Set(ref this.selected, value);
            this.HasSelection = value is not null;
        }
    }

    /// <summary>Whether the action bar has anything to act on.</summary>
    public bool HasSelection
    {
        get => this.hasSelection;
        private set => this.Set(ref this.hasSelection, value);
    }

    /// <summary>Which slice of the board is listed. Opens on Steve's own decisions.</summary>
    public BoardView View
    {
        get => this.view;
        set => this.Set(ref this.view, value);
    }

    /// <summary>How many tasks want Steve, shown on the filter button.</summary>
    public int NeedsYouCount
    {
        get => this.needsYouCount;
        set => this.Set(ref this.needsYouCount, value);
    }

    /// <summary>How many tasks are open.</summary>
    public int OpenCount
    {
        get => this.openCount;
        set => this.Set(ref this.openCount, value);
    }

    /// <summary>How many tasks are blocked.</summary>
    public int BlockedCount
    {
        get => this.blockedCount;
        set => this.Set(ref this.blockedCount, value);
    }

    /// <summary>How many tasks the board holds in total.</summary>
    public int AllCount
    {
        get => this.allCount;
        set => this.Set(ref this.allCount, value);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>One recorded pass of a proactive service.</summary>
public sealed record WorkerRun(
    DateTimeOffset RanAt,
    string Status,
    string Trace,
    Guid TraceId,
    int Produced,
    int Egress,
    int Alerts,
    double Seconds,
    double BarHeight)
{
    /// <summary>When it ran, in local time.</summary>
    public string When => $"{this.RanAt:ddd dd MMM · HH:mm}";

    /// <summary>How long it took, said the way a person would.</summary>
    public string Elapsed => this.Seconds switch
    {
        < 1 => "instant",
        < 90 => $"{this.Seconds:0.0}s",
        _ => $"{this.Seconds / 60:0} min",
    };

    /// <summary>Whether this pass wants a look.</summary>
    /// <remarks>
    /// Status cannot answer this: a rate-limited scout pass is Completed and still the one
    /// worth opening. Without it you have to open every pass to find the bad one.
    /// </remarks>
    public bool HasAlerts => this.Alerts > 0;

    /// <summary>The outcome line under the timestamp.</summary>
    public string Outcome
    {
        get
        {
            var parts = new List<string> { this.Status, this.Elapsed };
            if (this.Produced > 0)
            {
                parts.Add($"{this.Produced} produced");
            }

            if (this.Alerts > 0)
            {
                parts.Add(this.Alerts == 1 ? "1 alert" : $"{this.Alerts} alerts");
            }

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>One proactive service and what it has been doing.</summary>
/// <remarks>A record so <see cref="Reconcile"/> can tell an unchanged poll from a change.</remarks>
public sealed record WorkerRow(
    string ServiceName,
    string LastStatus,
    string Age,
    int Runs,
    IReadOnlyList<WorkerRun> Recent,
    string Cadence,
    string Due,
    bool IsOverdue,
    int TotalProduced,
    int TotalEgress,
    int TotalAlerts)
{
    /// <summary>The summary line under the service name.</summary>
    public string Detail => $"{this.LastStatus} · {this.Age} · {this.Runs} run{(this.Runs == 1 ? string.Empty : "s")}";

    /// <summary>
    /// Cadence and when it is next expected. Without this the panel could say a service
    /// last ran five days ago but not whether that was its schedule — which is the entire
    /// judgement it exists to support.
    /// </summary>
    public string Schedule => string.IsNullOrEmpty(this.Cadence)
        ? "cadence unknown"
        : $"{this.Cadence} · {this.Due}";

    /// <summary>What this service has actually done, over its whole history.</summary>
    /// <remarks>
    /// A collector that has run forty times and produced nothing is a different thing from
    /// one that has produced two hundred facts, and the run count alone cannot tell them
    /// apart.
    /// </remarks>
    public string Totals
    {
        get
        {
            var parts = new List<string>();
            if (this.TotalProduced > 0)
            {
                parts.Add($"{this.TotalProduced} produced");
            }

            if (this.TotalEgress > 0)
            {
                parts.Add($"{this.TotalEgress} reached out");
            }

            if (this.TotalAlerts > 0)
            {
                parts.Add(this.TotalAlerts == 1 ? "1 alert" : $"{this.TotalAlerts} alerts");
            }

            return parts.Count == 0 ? "nothing produced yet" : string.Join(" · ", parts);
        }
    }

    /// <summary>Whether any recorded pass alerted.</summary>
    public bool HasAlerts => this.TotalAlerts > 0;

    /// <summary>Oldest first, so the strip reads left to right like a timeline.</summary>
    public IReadOnlyList<WorkerRun> Strip => this.Recent.Reverse().ToList();

    /// <inheritdoc />
    /// <remarks>
    /// A record's synthesised equality would compare <see cref="Recent"/> by reference and
    /// call every poll a change, which is the flicker this exists to avoid.
    /// </remarks>
    public bool Equals(WorkerRow? other)
    {
        return other is not null
            && this.ServiceName == other.ServiceName
            && this.LastStatus == other.LastStatus
            && this.Age == other.Age
            && this.Runs == other.Runs
            && this.Recent.SequenceEqual(other.Recent);
    }

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(this.ServiceName, this.LastStatus, this.Age, this.Runs);
}

/// <summary>One event in a replayed proactive pass, positioned in time.</summary>
/// <remarks>
/// A pass is read after the fact, so the question is not "what happened" but "where did
/// the time go and what went wrong". Each row therefore carries its offset from the start
/// of the pass, the gap since the previous event as a proportional bar, and whether it is
/// the thing worth noticing.
/// </remarks>
public sealed record PassEvent(
    string Time,
    string Offset,
    string Type,
    string Label,
    string Status,
    double BarLeft,
    double BarWidth,
    bool IsAlert)
{
    /// <summary>
    /// Where the bar sits on the track, so the row reads as a waterfall rather than a
    /// list. A bar that always starts at the left edge encodes duration and throws away
    /// when it happened, which is the half that shows a pass spending four seconds
    /// waiting on one feed.
    /// </summary>
    public Avalonia.Thickness BarMargin => new(this.BarLeft, 0, 0, 0);
}

/// <summary>The headline for a pass: what it cost and what it produced.</summary>
public sealed record PassSummary(
    string Duration,
    int Egress,
    int Produced,
    int Alerts)
{
    /// <summary>Empty state, before a pass is chosen.</summary>
    public static readonly PassSummary none = new("—", 0, 0, 0);

    /// <summary>Whether a pass is actually being shown, rather than the empty state.</summary>
    /// <remarks>
    /// Without this the headline reads "— elapsed · 0 produced · 0 reached out" over a
    /// pane that is telling you to pick a pass: three zeros that look like a finding.
    /// </remarks>
    public bool HasRun => !ReferenceEquals(this, none);

    /// <summary>Whether anything in the pass wants attention.</summary>
    public bool HasAlerts => this.Alerts > 0;

    /// <summary>How the alert count reads.</summary>
    public string AlertLine =>
        this.Alerts == 1 ? "1 needs a look" : $"{this.Alerts} need a look";
}

/// <summary>Everything the window binds to.</summary>
public sealed class WindowState : INotifyPropertyChanged
{
    private string workerTraceMessage = string.Empty;
    private string activityMessage = string.Empty;
    private PassSummary passSummary = PassSummary.none;

    /// <summary>What the trace pane is showing, or why it is showing nothing.</summary>
    public string WorkerTraceMessage
    {
        get => this.workerTraceMessage;
        set
        {
            if (this.workerTraceMessage == value)
            {
                return;
            }

            this.workerTraceMessage = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.WorkerTraceMessage)));
        }
    }

    /// <summary>The chart's window and resolution, said plainly under it.</summary>
    public string ActivityMessage
    {
        get => this.activityMessage;
        set
        {
            this.activityMessage = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.ActivityMessage)));
        }
    }

    /// <summary>What the selected pass cost and produced.</summary>
    public PassSummary PassSummary
    {
        get => this.passSummary;
        set
        {
            this.passSummary = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.PassSummary)));
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The conversation, oldest first.</summary>
    public ObservableCollection<Message> Messages { get; } = [];

    /// <summary>Pending surfacings and approvals.</summary>
    public ObservableCollection<SidebarItem> Attention { get; } = [];

    /// <summary>The active belief ledger.</summary>
    public ObservableCollection<SidebarItem> Beliefs { get; } = [];

    /// <summary>Live collaborative task-board state.</summary>
    public TaskBoardPanelState TaskBoards { get; } = new();

    /// <summary>The rolling activity chart's plotted series.</summary>
    public ObservableCollection<ActivitySeries> Activity { get; } = [];

    /// <summary>What the proactive tier has been doing, most recently active first.</summary>
    public ObservableCollection<WorkerRow> Workers { get; } = [];

    /// <summary>Passes of the selected service, newest first.</summary>
    public ObservableCollection<WorkerRun> SelectedWorkerRuns { get; } = [];

    /// <summary>
    /// The selected pass replayed from the durable event stream — what it actually did,
    /// not a summary of it.
    /// </summary>
    public ObservableCollection<PassEvent> WorkerTrace { get; } = [];
}
