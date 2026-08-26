using Dami.Contracts.TaskBoard;
using Microsoft.Extensions.Logging;

namespace Dami.Core.BoardImport;

/// <summary>Writes a parsed blueprint onto the task board, and can be run again safely.</summary>
/// <remarks>
/// The board reaches every state through a guarded mutation — a claim needs its
/// prerequisites done, a completion needs its claimant, its criteria, and its children —
/// so an import cannot simply write the states it wants. It asks for one legal step per
/// task per pass and repeats until a pass changes nothing. That converges without the
/// importer having to topologically sort prerequisites against containment, and whatever
/// is still unreached when it stops is exactly what the board's own rules forbid, which is
/// reported rather than forced.
/// </remarks>
public sealed class TodoBoardImporter
{
    /// <summary>Bounds the fixed-point loop; the tree is five deep and passes converge fast.</summary>
    private const int MAX_PASSES = 32;

    private readonly ITaskBoardStore store;
    private readonly TimeProvider clock;
    private readonly ILogger<TodoBoardImporter> logger;

    /// <summary>Creates an importer.</summary>
    public TodoBoardImporter(
        ITaskBoardStore store,
        TimeProvider clock,
        ILogger<TodoBoardImporter> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.store = store;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Applies a plan, creating the board if it is not there yet.</summary>
    /// <param name="plan">The board to write and the states to reach.</param>
    /// <param name="importer">The actor running the import.</param>
    /// <param name="revision">The source revision, recorded in the activity it appends.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>What the run did.</returns>
    public async Task<TodoImportReport> ImportAsync(
        TodoImportPlan plan,
        TaskActor importer,
        string revision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        var existing = await this.store.FindAsync(plan.Draft.BoardId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            await this.store.CreateAsync(plan.Draft, cancellationToken).ConfigureAwait(false);
            this.logger.LogInformation("Created board {Board} from TODO.md at {Revision}.",
                plan.Draft.BoardId, revision);
        }

        var conflicts = new List<string>();
        var applied = await this.ConvergeAsync(plan, importer, revision, conflicts, cancellationToken)
            .ConfigureAwait(false);

        return new TodoImportReport(
            plan.Draft.BoardId, existing is null, plan.Desired.Count, applied, conflicts, plan.Anomalies);
    }

    /// <summary>Repeats passes until one changes nothing.</summary>
    private async Task<int> ConvergeAsync(
        TodoImportPlan plan,
        TaskActor importer,
        string revision,
        List<string> conflicts,
        CancellationToken cancellationToken)
    {
        // Deepest first, so a parent's children are already done when its completion is tried.
        var order = plan.Desired.OrderByDescending(task => task.Depth).ToList();
        var applied = 0;
        for (var pass = 0; pass < MAX_PASSES; pass++)
        {
            var snapshot = await this.store.FindAsync(plan.Draft.BoardId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("The board vanished mid-import.");

            var context = new PassContext(
                Index(snapshot), importer, revision, snapshot.BoardId, Parents(plan.Draft), ParentOf(snapshot));
            var moved = await this.PassAsync(order, context, cancellationToken).ConfigureAwait(false);
            applied += moved.Applied;
            if (moved.Applied == 0)
            {
                conflicts.AddRange(moved.Conflicts);
                return applied;
            }
        }

        this.logger.LogWarning("Import did not converge in {Passes} passes.", MAX_PASSES);
        return applied;
    }

    private async Task<(int Applied, List<string> Conflicts)> PassAsync(
        List<DesiredTask> order,
        PassContext context,
        CancellationToken cancellationToken)
    {
        var applied = 0;
        var conflicts = new List<string>();
        foreach (var desired in order)
        {
            if (!context.ById.TryGetValue(desired.TaskId, out var actual))
            {
                var added = await this.AddMissingAsync(desired, context, conflicts, cancellationToken)
                    .ConfigureAwait(false);
                applied += added ? 1 : 0;
                continue;
            }

            var step = ImportStep.Next(desired, actual, context.Importer);
            if (step.Kind == ImportStepKind.Conflict)
            {
                conflicts.Add(step.Detail);
                continue;
            }

            if (step.Kind != ImportStepKind.None
                && await this.ApplyAsync(step, desired, actual, context, cancellationToken)
                    .ConfigureAwait(false))
            {
                applied++;
            }
        }

        return (applied, conflicts);
    }

    private async Task<bool> ApplyAsync(
        ImportStep step,
        DesiredTask desired,
        BoardTask actual,
        PassContext context,
        CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        return step.Kind switch
        {
            ImportStepKind.Claim => await this.store.TryClaimAsync(
                actual.TaskId,
                actual.Version,
                step.Actor,
                ClaimedAt(desired, actual.CreatedAt, now),
                ImportTag(context),
                cancellationToken).ConfigureAwait(false),
            ImportStepKind.SatisfyCriteria => await this.SatisfyAsync(
                actual, step.Actor, now, cancellationToken).ConfigureAwait(false),
            ImportStepKind.Complete => await this.store.TryCompleteAsync(
                actual.TaskId, actual.Version, step.Actor, now, ImportTag(context), cancellationToken)
                .ConfigureAwait(false),
            ImportStepKind.Block or ImportStepKind.Cancel or ImportStepKind.Reopen => await this.store.TrySetStatusAsync(
                actual.TaskId,
                actual.Version,
                StatusFor(step.Kind),
                step.Actor,
                $"{step.Detail} {ImportTag(context)}",
                now,
                cancellationToken).ConfigureAwait(false),
            _ => false,
        };
    }

    private static TaskBoardStatus StatusFor(ImportStepKind kind)
    {
        return kind switch
        {
            ImportStepKind.Block => TaskBoardStatus.Blocked,
            ImportStepKind.Cancel => TaskBoardStatus.Cancelled,
            _ => TaskBoardStatus.Open,
        };
    }

    /// <summary>The provenance every imported mutation carries.</summary>
    private static string ImportTag(PassContext context)
    {
        return $"[imported from TODO.md at {context.Revision}]";
    }

    /// <summary>
    /// One criterion per pass. Each mutation bumps the task's version, so ticking two in a
    /// row with the version read at the start of the pass would have the second rejected.
    /// </summary>
    private async Task<bool> SatisfyAsync(
        BoardTask actual,
        TaskActor actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = actual.AcceptanceCriteria.FirstOrDefault(criterion => !criterion.IsSatisfied);
        return pending is not null
            && await this.store.TrySetCriterionAsync(
                pending.CriterionId, actual.Version, true, actor, now, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// The date the file recorded, so an imported claim keeps when it was made — but never
    /// earlier than the board itself.
    /// </summary>
    /// <remarks>
    /// A claim writes <c>updated_at</c>, and the schema requires <c>updated_at >= created_at</c>.
    /// A board created today cannot hold a claim made last week, so the timestamp is clamped
    /// to the board's creation. The original date is not lost: the mapper writes "Claimed in
    /// TODO.md by X on YYYY-MM-DD" into the task's description, which is where a date that
    /// predates the record it lives in honestly belongs.
    /// </remarks>
    /// <summary>
    /// The file's claim date, clamped to the task's own creation: a task cannot have been
    /// claimed before it existed on the board, and the schema refuses the row if it says so.
    /// </summary>
    private static DateTimeOffset ClaimedAt(
        DesiredTask desired,
        DateTimeOffset taskCreatedAt,
        DateTimeOffset now)
    {
        if (desired.ClaimedOn is null)
        {
            return now < taskCreatedAt ? taskCreatedAt : now;
        }

        var stated = new DateTimeOffset(
            desired.ClaimedOn.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return stated < taskCreatedAt ? taskCreatedAt : stated;
    }

    private sealed record PassContext(
        Dictionary<Guid, BoardTask> ById,
        TaskActor Importer,
        string Revision,
        Guid BoardId,
        Dictionary<Guid, (Guid? ParentId, BoardTaskDraft Draft)> Parents,
        Dictionary<Guid, Guid?> ParentOf);

    /// <summary>
    /// A task the file has and the board lacks is added — one node, no subtree, so its
    /// children are added on the next pass once it is there to hang them on. It waits
    /// while its parent or a prerequisite is still missing, and is reported only when
    /// nothing else moved either.
    /// </summary>
    private async Task<bool> AddMissingAsync(
        DesiredTask desired,
        PassContext context,
        List<string> conflicts,
        CancellationToken cancellationToken)
    {
        var name = desired.TodoId ?? desired.TaskId.ToString();
        var blocker = WhyNotPlaceable(desired, context);
        if (blocker is not null)
        {
            conflicts.Add($"{name} is not on the board and {blocker}.");
            return false;
        }

        var (parentId, draft) = context.Parents[desired.TaskId];
        if (desired.TodoId is not null && SameIdOnBoard(desired.TodoId, parentId, context) is { } twin)
        {
            conflicts.Add($"{name} is already on the board as {twin.TaskId:N} under a different id; not added again.");
            return false;
        }

        var single = new BoardTaskDraft(
            draft.TaskId, draft.Title, draft.Description, draft.Priority, draft.Position,
            draft.SubTaskOrdering, draft.PrerequisiteTaskIds, draft.AcceptanceCriteria, []);
        var added = await this.store.TryAddTaskAsync(
            context.BoardId, parentId, single, context.Importer, this.clock.GetUtcNow(),
            ImportTag(context), cancellationToken).ConfigureAwait(false);
        if (!added)
        {
            conflicts.Add($"{name} is not on the board and the board refused to add it.");
        }

        return added;
    }

    /// <summary>
    /// A task born on the board before ids were made stable carries its TODO id only in its
    /// title. Adding the file's copy beside it would make two; the board's one stands.
    /// </summary>
    private static BoardTask? SameIdOnBoard(string todoId, Guid? parentId, PassContext context)
    {
        return context.ById.Values.FirstOrDefault(task =>
            task.Status != TaskBoardStatus.Cancelled
            && task.Title.StartsWith(todoId + " ", StringComparison.Ordinal)
            && context.ParentOf.GetValueOrDefault(task.TaskId) == parentId);
    }

    private static string? WhyNotPlaceable(DesiredTask desired, PassContext context)
    {
        if (!context.Parents.TryGetValue(desired.TaskId, out var place))
        {
            return "the plan does not place it";
        }

        if (place.ParentId is not null && !context.ById.ContainsKey(place.ParentId.Value))
        {
            return "neither is its parent";
        }

        return place.Draft.PrerequisiteTaskIds.Any(id => !context.ById.ContainsKey(id))
            ? "it waits on a prerequisite that is not either"
            : null;
    }

    /// <summary>Where every drafted task belongs: its parent's id, or null at the root.</summary>
    private static Dictionary<Guid, (Guid? ParentId, BoardTaskDraft Draft)> Parents(TaskBoardDraft board)
    {
        var parents = new Dictionary<Guid, (Guid?, BoardTaskDraft)>();
        Place(board.Tasks, null, parents);
        return parents;
    }

    private static void Place(
        IReadOnlyList<BoardTaskDraft> tasks,
        Guid? parentId,
        Dictionary<Guid, (Guid? ParentId, BoardTaskDraft Draft)> parents)
    {
        foreach (var task in tasks)
        {
            parents[task.TaskId] = (parentId, task);
            Place(task.SubTasks, task.TaskId, parents);
        }
    }

    /// <summary>Each board task's parent, so a twin is only a twin under the same parent.</summary>
    private static Dictionary<Guid, Guid?> ParentOf(TaskBoardSnapshot snapshot)
    {
        var parents = new Dictionary<Guid, Guid?>();
        Walk(snapshot.Tasks, null, parents);
        return parents;
    }

    private static void Walk(IReadOnlyList<BoardTask> tasks, Guid? parentId, Dictionary<Guid, Guid?> parents)
    {
        foreach (var task in tasks)
        {
            parents[task.TaskId] = parentId;
            Walk(task.SubTasks, task.TaskId, parents);
        }
    }

    private static Dictionary<Guid, BoardTask> Index(TaskBoardSnapshot snapshot)
    {
        var byId = new Dictionary<Guid, BoardTask>();
        Index(snapshot.Tasks, byId);
        return byId;
    }

    private static void Index(IReadOnlyList<BoardTask> tasks, Dictionary<Guid, BoardTask> byId)
    {
        foreach (var task in tasks)
        {
            byId[task.TaskId] = task;
            Index(task.SubTasks, byId);
        }
    }
}
