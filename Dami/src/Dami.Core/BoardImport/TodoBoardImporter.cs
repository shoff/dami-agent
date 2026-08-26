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

            var context = new PassContext(Index(snapshot), importer, revision, snapshot.CreatedAt);
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
                conflicts.Add($"{desired.TodoId ?? desired.TaskId.ToString()} is not on the board.");
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
                ClaimedAt(desired, context.BoardCreatedAt, now),
                ImportTag(context),
                cancellationToken).ConfigureAwait(false),
            ImportStepKind.SatisfyCriteria => await this.SatisfyAsync(
                actual, step.Actor, now, cancellationToken).ConfigureAwait(false),
            ImportStepKind.Complete => await this.store.TryCompleteAsync(
                actual.TaskId, actual.Version, step.Actor, now, ImportTag(context), cancellationToken)
                .ConfigureAwait(false),
            ImportStepKind.Block => await this.store.TrySetStatusAsync(
                actual.TaskId,
                actual.Version,
                TaskBoardStatus.Blocked,
                step.Actor,
                $"{step.Detail} {ImportTag(context)}",
                now,
                cancellationToken).ConfigureAwait(false),
            _ => false,
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
    private static DateTimeOffset ClaimedAt(
        DesiredTask desired,
        DateTimeOffset boardCreatedAt,
        DateTimeOffset now)
    {
        if (desired.ClaimedOn is null)
        {
            return now < boardCreatedAt ? boardCreatedAt : now;
        }

        var stated = new DateTimeOffset(
            desired.ClaimedOn.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return stated < boardCreatedAt ? boardCreatedAt : stated;
    }

    private sealed record PassContext(
        Dictionary<Guid, BoardTask> ById,
        TaskActor Importer,
        string Revision,
        DateTimeOffset BoardCreatedAt);

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
