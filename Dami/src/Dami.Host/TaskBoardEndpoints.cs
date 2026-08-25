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
        app.MapPost("/task-boards/tasks/{taskId:guid}/claim", ClaimAsync);
        app.MapPut("/task-boards/criteria/{criterionId:guid}", SetCriterionAsync);
        app.MapPost("/task-boards/tasks/{taskId:guid}/complete", CompleteAsync);
        app.MapPut("/task-boards/tasks/{taskId:guid}/status", SetStatusAsync);
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
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var invalid = ValidatePlanning(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var actor = new TaskActor(request.ActorId, request.ActorKind);
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

    private static async Task<IResult> ClaimAsync(
        Guid taskId,
        TaskBoardMutationRequest request,
        ITaskBoardStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateMutation(
            request.ExpectedVersion, request.ActorId, request.ActorKind);
        if (invalid is not null)
        {
            return invalid;
        }

        var actor = new TaskActor(request.ActorId, request.ActorKind);
        var updated = await store.TryClaimAsync(
            taskId, request.ExpectedVersion, actor, clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        return MutationResult(updated);
    }

    private static async Task<IResult> SetCriterionAsync(
        Guid criterionId,
        TaskBoardCriterionRequest request,
        ITaskBoardStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateMutation(
            request.ExpectedVersion, request.ActorId, request.ActorKind);
        if (invalid is not null)
        {
            return invalid;
        }

        var actor = new TaskActor(request.ActorId, request.ActorKind);
        var updated = await store.TrySetCriterionAsync(
            criterionId, request.ExpectedVersion, request.IsSatisfied,
            actor, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return MutationResult(updated);
    }

    private static async Task<IResult> CompleteAsync(
        Guid taskId,
        TaskBoardMutationRequest request,
        ITaskBoardStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateMutation(
            request.ExpectedVersion, request.ActorId, request.ActorKind);
        if (invalid is not null)
        {
            return invalid;
        }

        var actor = new TaskActor(request.ActorId, request.ActorKind);
        var updated = await store.TryCompleteAsync(
            taskId, request.ExpectedVersion, actor, clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        return MutationResult(updated);
    }

    private static async Task<IResult> SetStatusAsync(
        Guid taskId,
        TaskBoardStatusRequest request,
        ITaskBoardStore store,
        TimeProvider clock,
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

        var actor = new TaskActor(request.ActorId, request.ActorKind);
        var updated = await store.TrySetStatusAsync(
            taskId, request.ExpectedVersion, request.Status, actor,
            request.Detail, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return MutationResult(updated);
    }

    private static IResult? ValidateMutation(
        long expectedVersion,
        string actorId,
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

    private static IResult MutationResult(bool updated)
    {
        return updated
            ? Results.Ok(new { updated = true })
            : Results.Conflict(new { updated = false });
    }
}

internal sealed record TaskBoardMutationRequest(
    long ExpectedVersion,
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
