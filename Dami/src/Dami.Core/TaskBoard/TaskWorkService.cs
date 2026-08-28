using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.TaskBoard;
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
    private readonly IFrontierChat frontier;
    private readonly IIdentityProvider identity;
    private readonly TimeProvider clock;

    /// <summary>Creates the service.</summary>
    public TaskWorkService(
        ITaskBoardStore store,
        ITurnRunner runner,
        TimeProvider clock,
        IFrontierChat frontier,
        IIdentityProvider identity)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(frontier);
        ArgumentNullException.ThrowIfNull(identity);

        this.store = store;
        this.runner = runner;
        this.clock = clock;
        this.frontier = frontier;
        this.identity = identity;
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
            var (traceId, answer) = planner == FeaturePlannerKind.Frontier
                ? await this.AskFrontierAsync(prompt, cancellationToken).ConfigureAwait(false)
                : await this.AskLocalAsync(prompt, cancellationToken).ConfigureAwait(false);
            await this.LogAsync(
                task.TaskId, TaskBoardActivityKind.TaskWorkFinished, actor,
                $"advisory run finished on {planner} · trace {traceId:N}",
                cancellationToken).ConfigureAwait(false);
            return new TaskWorkOutcome(true, traceId, answer, string.Empty);
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

    private async Task<(Guid TraceId, string Answer)> AskLocalAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        var result = await this.runner.RunAsync(prompt, cancellationToken).ConfigureAwait(false);
        return (result.TraceId, result.Answer);
    }

    /// <remarks>
    /// The board's own text only — task title, scope, and acceptance criteria. No
    /// retrieved memory joins it, which is what keeps this Egressable without a
    /// disclosure step, exactly as the frontier chat turn is.
    /// </remarks>
    private async Task<(Guid TraceId, string Answer)> AskFrontierAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        var traceId = Guid.NewGuid();
        var answer = await this.frontier.CompleteAsync(
            new FrontierPrompt(
                $"{this.identity.FrontierVoice}\n\n{prompt}", "task board advisory run",
                PrivacyClass.Egressable, traceId, ExecutionOrigin.UserTurn),
            cancellationToken).ConfigureAwait(false);
        return (traceId, answer);
    }

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
