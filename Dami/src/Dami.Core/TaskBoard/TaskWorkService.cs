using Dami.Contracts.Privacy;
using Dami.Contracts.TaskBoard;
using Dami.Core.Frontier;
using Dami.Core.Turns;

namespace Dami.Core.TaskBoard;

/// <summary>What one advisory run against a board task produced.</summary>
public sealed record TaskWorkOutcome(bool Ran, Guid TraceId, string Answer, string Reason)
{
    /// <summary>A run that never started, and why.</summary>
    public static TaskWorkOutcome Refused(string reason) =>
        new(false, Guid.Empty, string.Empty, reason);
}

/// <summary>Runs one turn against one board task, and records that it happened.</summary>
/// <remarks>
/// This is the "work this task now" path, and it is deliberately advisory (V1). It reads
/// the board's own snapshot rather than trusting anything the caller sent, runs a single
/// turn, and writes the trace id onto the board. It never claims, completes, or changes a
/// status: the completion gate in 028 — every criterion satisfied, every child finished,
/// every prerequisite done — stays asserted by a hand. Giving a run the tools and the
/// authority to finish work is V2, and needs a decision record before it needs code.
/// </remarks>
public sealed class TaskWorkService
{
    private readonly ITaskBoardStore store;
    private readonly ITurnRunner runner;
    private readonly IAugmentedTurn augmented;
    private readonly TimeProvider clock;

    /// <summary>Creates the service.</summary>
    public TaskWorkService(
        ITaskBoardStore store,
        ITurnRunner runner,
        TimeProvider clock,
        IAugmentedTurn augmented)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(augmented);

        this.store = store;
        this.runner = runner;
        this.clock = clock;
        this.augmented = augmented;
    }

    /// <summary>Works one task, or explains why it did not.</summary>
    public async Task<TaskWorkOutcome> RunAsync(
        Guid boardId,
        Guid taskId,
        TaskActor actor,
        FeaturePlannerKind planner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var board = await this.store.FindAsync(boardId, cancellationToken).ConfigureAwait(false);
        if (board is null)
        {
            return TaskWorkOutcome.Refused("that board no longer exists");
        }

        var task = Find(board.Tasks, taskId);
        if (task is null)
        {
            return TaskWorkOutcome.Refused("that task is not on this board");
        }

        if (task.Status is TaskBoardStatus.Done or TaskBoardStatus.Cancelled)
        {
            return TaskWorkOutcome.Refused($"that task is already {task.Status}");
        }

        return await this.RunTurnAsync(board, task, actor, planner, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TaskWorkOutcome> RunTurnAsync(
        TaskBoardSnapshot board,
        BoardTask task,
        TaskActor actor,
        FeaturePlannerKind planner,
        CancellationToken cancellationToken)
    {
        await this.LogAsync(
            task.TaskId, TaskBoardActivityKind.TaskWorkStarted, actor,
            $"advisory run requested on \"{task.Title}\"", cancellationToken).ConfigureAwait(false);

        try
        {
            var prompt = TaskWorkPrompt.Build(board.Title, task);
            var run = planner == FeaturePlannerKind.Frontier
                ? await this.AskFrontierWithLocalSupportAsync(prompt, cancellationToken)
                    .ConfigureAwait(false)
                : await this.AskLocalAsync(prompt, cancellationToken).ConfigureAwait(false);
            await this.LogAsync(
                task.TaskId, TaskBoardActivityKind.TaskWorkFinished, actor,
                $"advisory run finished · {run.How} · trace {run.TraceId:N}",
                cancellationToken).ConfigureAwait(false);
            return new TaskWorkOutcome(true, run.TraceId, run.Answer, string.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A turn that threw still happened, and the board is the record of what
            // happened. Leaving only TaskWorkStarted would read as a run that never
            // came back.
            await this.LogAsync(
                task.TaskId, TaskBoardActivityKind.TaskWorkFinished, actor,
                $"advisory run failed: {exception.Message}", cancellationToken).ConfigureAwait(false);
            return TaskWorkOutcome.Refused(exception.Message);
        }
    }

    private async Task<RunOutcome> AskLocalAsync(string prompt, CancellationToken cancellationToken)
    {
        var result = await this.runner.RunAsync(prompt, cancellationToken).ConfigureAwait(false);
        var items = result.Context.Memories.Count + result.Context.Beliefs.Count;
        return new RunOutcome(
            result.TraceId, result.Answer, $"answered locally on {items} retrieved item(s)");
    }

    /// <summary>The local sidecar does the legwork; the frontier writes the answer.</summary>
    /// <remarks>
    /// The two models are used together, not chosen between. Retrieval, reranking, and
    /// the D-012 redaction all run on this host, and what the frontier receives is what
    /// the local model prepared — stored hash-pinned, so the egress is auditable after
    /// the fact rather than merely promised. That is why this routes through
    /// <see cref="AugmentedFrontierTurn"/> instead of calling <c>IFrontierChat</c>: a
    /// bare call would send the board text with none of Dami's own knowledge behind it
    /// and leave no disclosure record.
    ///
    /// If the subscription is not there — not signed in, the CLI missing, the process
    /// failing — the local model takes over rather than the run being lost, and the board
    /// says which happened. An <see cref="EgressRefusedException"/> is deliberately not
    /// caught: a privacy boundary refusing is an answer, not an outage to route around.
    /// </remarks>
    private async Task<RunOutcome> AskFrontierWithLocalSupportAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await this.augmented.RunAsync(prompt, cancellationToken)
                .ConfigureAwait(false);
            return new RunOutcome(
                result.TraceId, result.Answer,
                $"locally retrieved {result.ContextItems} item(s), answered at the frontier");
        }
        catch (EgressRefusedException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var local = await this.AskLocalAsync(prompt, cancellationToken).ConfigureAwait(false);
            return local with
            {
                How = $"frontier unavailable ({exception.Message}); answered locally instead",
            };
        }
    }

    private sealed record RunOutcome(Guid TraceId, string Answer, string How);

    private Task LogAsync(
        Guid taskId,
        TaskBoardActivityKind kind,
        TaskActor actor,
        string detail,
        CancellationToken cancellationToken)
    {
        return this.store.TryLogWorkAsync(
            taskId, kind, actor, detail, this.clock.GetUtcNow(), cancellationToken);
    }

    private static BoardTask? Find(IReadOnlyList<BoardTask> tasks, Guid taskId)
    {
        foreach (var task in tasks)
        {
            if (task.TaskId == taskId)
            {
                return task;
            }

            var found = Find(task.SubTasks, taskId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
