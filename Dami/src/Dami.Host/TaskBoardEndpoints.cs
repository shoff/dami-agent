using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.TaskBoard;
using Dami.Core.TaskBoard;

namespace Dami.Host;

internal static class TaskBoardEndpoints
{
    private const int DEFAULT_LIST_LIMIT = 20;
    private const int MAX_LIST_LIMIT = 100;
    private const int DEFAULT_ACTIVITY_LIMIT = 100;
    private const int MAX_ACTIVITY_LIMIT = 500;

    internal static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/task-boards", ListAsync);
        app.MapGet("/task-boards/{boardId:guid}", FindAsync);
        app.MapGet("/task-boards/{boardId:guid}/activity", ActivityAsync);
        app.MapPost("/task-boards/plan", PlanAsync);
        app.MapPost("/task-boards/{boardId:guid}/tasks", AddTaskAsync);
        app.MapPost("/task-boards/tasks/{taskId:guid}/claim", ClaimAsync);
        app.MapPut("/task-boards/criteria/{criterionId:guid}", SetCriterionAsync);
        app.MapPost("/task-boards/tasks/{taskId:guid}/criteria", AddCriterionAsync);
        app.MapPost("/task-boards/tasks/{taskId:guid}/complete", CompleteAsync);
        app.MapPut("/task-boards/tasks/{taskId:guid}/status", SetStatusAsync);
        app.MapPost("/task-boards/{boardId:guid}/tasks/{taskId:guid}/work", WorkAsync);
    }

    private static async Task<IResult> ListAsync(
        ITaskBoardStore store,
        int? limit,
        CancellationToken cancellationToken)
    {
        var requested = limit ?? DEFAULT_LIST_LIMIT;
        if (requested is <= 0 or > MAX_LIST_LIMIT)
        {
            return Results.BadRequest(new { error = $"limit must be between 1 and {MAX_LIST_LIMIT}" });
        }

        var summaries = new List<TaskBoardSummary>(requested);
        await foreach (var summary in store.ListRecentAsync(requested, cancellationToken)
            .ConfigureAwait(false))
        {
            summaries.Add(summary);
        }

        return Results.Ok(summaries);
    }

    private static async Task<IResult> FindAsync(
        Guid boardId,
        ITaskBoardStore store,
        CancellationToken cancellationToken)
    {
        var board = await store.FindAsync(boardId, cancellationToken).ConfigureAwait(false);
        return board is null ? Results.NotFound() : Results.Ok(board);
    }

    private static async Task<IResult> ActivityAsync(
        Guid boardId,
        ITaskBoardStore store,
        int? limit,
        CancellationToken cancellationToken)
    {
        var requested = limit ?? DEFAULT_ACTIVITY_LIMIT;
        if (requested is <= 0 or > MAX_ACTIVITY_LIMIT)
        {
            return Results.BadRequest(
                new { error = $"limit must be between 1 and {MAX_ACTIVITY_LIMIT}" });
        }

        var activity = new List<TaskBoardActivity>(requested);
        await foreach (var item in store.ActivityAsync(boardId, requested, cancellationToken)
            .ConfigureAwait(false))
        {
            activity.Add(item);
        }

        return Results.Ok(activity);
    }

    private static async Task<IResult> PlanAsync(
        TaskBoardPlanningRequest request,
        FeaturePlanningService planningService,
        TaskBoardActorResolver actors,
        TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var invalid = ValidatePlanning(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var actor = ResolveActor(actors, context, request.ActorId, request.ActorKind);
        if (actor is null)
        {
            return InvalidActorResult(actors);
        }

        var planningRequest = new FeaturePlanningRequest(
            request.RequestId, request.FeatureRequest, actor, clock.GetUtcNow(),
            request.Planner, request.Privacy, request.Origin);
        var boardId = await planningService.PlanAsync(planningRequest, cancellationToken)
            .ConfigureAwait(false);
        return Results.Created($"/task-boards/{boardId:D}", new { boardId });
    }

    private static IResult? ValidatePlanning(TaskBoardPlanningRequest request)
    {
        if (request.RequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.FeatureRequest))
        {
            return Results.BadRequest(new { error = "a request id and feature request are required" });
        }

        if (string.IsNullOrWhiteSpace(request.ActorId)
            || !Enum.IsDefined(request.ActorKind)
            || !Enum.IsDefined(request.Planner)
            || !Enum.IsDefined(request.Privacy)
            || !Enum.IsDefined(request.Origin))
        {
            return Results.BadRequest(new { error = "planning metadata is invalid" });
        }

        return null;
    }

    private static async Task<IResult> AddTaskAsync(
        Guid boardId,
        TaskBoardAddTaskRequest request,
        ITaskBoardStore store,
        TaskBoardActorResolver actors,
        TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.ActorId)
            || !Enum.IsDefined(request.ActorKind) || !Enum.IsDefined(request.Priority)
            || request.Position < 0)
        {
            return Results.BadRequest(new { error = "a title, a valid actor, priority, and position are required" });
        }

        var taskId = request.TaskId ?? Guid.NewGuid();
        var criteria = (request.Criteria ?? [])
            .Select((text, index) => new AcceptanceCriterionDraft(Guid.NewGuid(), text, index))
            .ToArray();
        var draft = new BoardTaskDraft(
            taskId, request.Title.Trim(), request.Description ?? string.Empty, request.Priority,
            request.Position, TaskOrdering.Ordered, request.PrerequisiteTaskIds ?? [], criteria, []);
        var actor = ResolveActor(actors, context, request.ActorId, request.ActorKind);
        if (actor is null)
        {
            return InvalidActorResult(actors);
        }

        var added = await store.TryAddTaskAsync(
            boardId, request.ParentTaskId, draft, actor, clock.GetUtcNow(), request.Detail, cancellationToken)
            .ConfigureAwait(false);
        return added
            ? Results.Created($"/task-boards/{boardId:D}", new { taskId })
            : Results.Conflict(new { updated = false });
    }

    private static async Task<IResult> ClaimAsync(
        Guid taskId,
        TaskBoardMutationRequest request,
        ITaskBoardStore store,
        TaskBoardActorResolver actors,
        TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedVersion <= 0)
        {
            return Results.BadRequest(new { error = "expectedVersion must be positive" });
        }

        var actor = actors.Resolve(context.User, request.ActorId, request.ActorKind);
        if (actor is null)
        {
            return InvalidActorResult(actors);
        }

        var updated = await store.TryClaimAsync(
            taskId, request.ExpectedVersion, actor, clock.GetUtcNow(), request.Detail,
            cancellationToken).ConfigureAwait(false);
        return MutationResult(updated);
    }

    private static async Task<IResult> AddCriterionAsync(
        Guid taskId,
        TaskBoardAddCriterionRequest request,
        ITaskBoardStore store,
        TaskBoardActorResolver actors,
        TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateMutation(request.ExpectedVersion, request.ActorId, request.ActorKind);
        if (invalid is not null)
        {
            return invalid;
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Results.BadRequest(new { error = "a criterion needs a description" });
        }

        var actor = ResolveActor(actors, context, request.ActorId, request.ActorKind);
        if (actor is null)
        {
            return InvalidActorResult(actors);
        }

        var added = await store.TryAddCriterionAsync(
            taskId, request.ExpectedVersion, request.Description, actor, clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        return MutationResult(added);
    }

    private static async Task<IResult> SetCriterionAsync(
        Guid criterionId,
        TaskBoardCriterionRequest request,
        ITaskBoardStore store,
        TaskBoardActorResolver actors,
        TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateMutation(
            request.ExpectedVersion, request.ActorId, request.ActorKind);
        if (invalid is not null)
        {
            return invalid;
        }

        var actor = ResolveActor(actors, context, request.ActorId, request.ActorKind);
        if (actor is null)
        {
            return InvalidActorResult(actors);
        }

        var updated = await store.TrySetCriterionAsync(
            criterionId, request.ExpectedVersion, request.IsSatisfied,
            actor, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return MutationResult(updated);
    }

    private static async Task<IResult> CompleteAsync(
        Guid taskId,
        TaskBoardMutationRequest request,
        ITaskBoardStore store,
        TaskBoardActorResolver actors,
        TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateMutation(
            request.ExpectedVersion, request.ActorId, request.ActorKind);
        if (invalid is not null)
        {
            return invalid;
        }

        var actor = ResolveActor(actors, context, request.ActorId, request.ActorKind);
        if (actor is null)
        {
            return InvalidActorResult(actors);
        }

        var updated = await store.TryCompleteAsync(
            taskId, request.ExpectedVersion, actor, clock.GetUtcNow(), request.Detail,
            cancellationToken).ConfigureAwait(false);
        return MutationResult(updated);
    }

    private static async Task<IResult> SetStatusAsync(
        Guid taskId,
        TaskBoardStatusRequest request,
        ITaskBoardStore store,
        TaskBoardActorResolver actors,
        TimeProvider clock,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateMutation(
            request.ExpectedVersion, request.ActorId, request.ActorKind);
        if (invalid is not null)
        {
            return invalid;
        }

        if (string.IsNullOrWhiteSpace(request.Detail)
            || request.Status is not (TaskBoardStatus.Open
                or TaskBoardStatus.Blocked or TaskBoardStatus.Cancelled))
        {
            return Results.BadRequest(
                new { error = "status must be Open, Blocked, or Cancelled and detail is required" });
        }

        var actor = ResolveActor(actors, context, request.ActorId, request.ActorKind);
        if (actor is null)
        {
            return InvalidActorResult(actors);
        }

        var updated = await store.TrySetStatusAsync(
            taskId, request.ExpectedVersion, request.Status, actor,
            request.Detail, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return MutationResult(updated);
    }

    private static IResult? ValidateMutation(
        long expectedVersion,
        string? actorId,
        TaskActorKind actorKind)
    {
        if (expectedVersion <= 0)
        {
            return Results.BadRequest(new { error = "expectedVersion must be positive" });
        }

        if (string.IsNullOrWhiteSpace(actorId) || !Enum.IsDefined(actorKind))
        {
            return Results.BadRequest(new { error = "a valid actor is required" });
        }

        return null;
    }

    private static TaskActor? ResolveActor(
        TaskBoardActorResolver actors,
        HttpContext context,
        string? actorId,
        TaskActorKind actorKind) => actors.Resolve(
            context.User, actorId, actorKind);

    /// <summary>
    /// "Work this task now": one advisory turn against one task. It takes no expected
    /// version because it mutates nothing — the run records itself on the board and
    /// leaves status, claim, and completion exactly where they were. Deliberately
    /// synchronous: the caller gets the trace id and the answer, and the same run is
    /// already visible in the execution event stream while it happens.
    /// </summary>
    private static async Task<IResult> WorkAsync(
        Guid boardId,
        Guid taskId,
        TaskBoardWorkRequest request,
        TaskWorkService work,
        TaskBoardActorResolver actors,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actor = actors.Resolve(context.User, request.ActorId, request.ActorKind);
        if (actor is null)
        {
            return InvalidActorResult(actors);
        }

        var outcome = await work
            .RunAsync(boardId, taskId, actor, request.Planner, cancellationToken)
            .ConfigureAwait(false);
        return outcome.Ran
            ? Results.Ok(new { ran = true, traceId = outcome.TraceId, answer = outcome.Answer })
            : Results.BadRequest(new { ran = false, reason = outcome.Reason });
    }

    private static IResult InvalidActorResult(TaskBoardActorResolver actors) =>
        actors.UsesAuthenticatedClaims
            ? Results.Forbid()
            : Results.BadRequest(new { error = "a valid actor is required" });

    private static IResult MutationResult(bool updated)
    {
        return updated
            ? Results.Ok(new { updated = true })
            : Results.Conflict(new { updated = false });
    }
}

internal sealed record TaskBoardWorkRequest(
    string? ActorId,
    TaskActorKind ActorKind,
    FeaturePlannerKind Planner = FeaturePlannerKind.Local);

internal sealed record TaskBoardMutationRequest(
    long ExpectedVersion,
    string? ActorId,
    TaskActorKind ActorKind,
    string? Detail = null);

internal sealed record TaskBoardAddTaskRequest(
    string Title,
    string ActorId,
    TaskActorKind ActorKind,
    Guid? ParentTaskId = null,
    Guid? TaskId = null,
    string? Description = null,
    TaskPriority Priority = TaskPriority.Normal,
    int Position = 0,
    IReadOnlyList<Guid>? PrerequisiteTaskIds = null,
    IReadOnlyList<string>? Criteria = null,
    string? Detail = null);

internal sealed record TaskBoardAddCriterionRequest(
    long ExpectedVersion,
    string Description,
    string ActorId,
    TaskActorKind ActorKind);

internal sealed record TaskBoardCriterionRequest(
    long ExpectedVersion,
    bool IsSatisfied,
    string ActorId,
    TaskActorKind ActorKind);

internal sealed record TaskBoardStatusRequest(
    long ExpectedVersion,
    TaskBoardStatus Status,
    string Detail,
    string ActorId,
    TaskActorKind ActorKind);

internal sealed record TaskBoardPlanningRequest(
    Guid RequestId,
    string FeatureRequest,
    string ActorId,
    TaskActorKind ActorKind,
    FeaturePlannerKind Planner,
    PrivacyClass Privacy,
    ExecutionOrigin Origin);
