using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.TaskBoard;

namespace Dami.Gui;

/// <summary>Result of one versioned task-board write.</summary>
public enum TaskBoardMutationOutcome
{
    /// <summary>The server committed the mutation.</summary>
    Updated,

    /// <summary>Another actor changed the task first.</summary>
    Conflict,
}

/// <summary>What one advisory "work this task now" run reported back.</summary>
public sealed record TaskWorkReply(bool Ran, Guid TraceId, string Answer, string? Reason);

/// <summary>Typed thin-client access to the runtime's task-board API.</summary>
public sealed class TaskBoardClient
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient httpClient;

    /// <summary>Creates a client over an injected HTTP boundary.</summary>
    public TaskBoardClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
    }

    /// <summary>Reads a bounded recent-board list with durable progress.</summary>
    public async Task<IReadOnlyList<TaskBoardSummary>> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        return await this.httpClient.GetFromJsonAsync<TaskBoardSummary[]>(
            $"/task-boards?limit={limit}", jsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    /// <summary>Reads one recursive board snapshot.</summary>
    public Task<TaskBoardSnapshot?> FindAsync(
        Guid boardId,
        CancellationToken cancellationToken) => this.httpClient.GetFromJsonAsync<TaskBoardSnapshot>(
            $"/task-boards/{boardId:D}", jsonOptions, cancellationToken);

    /// <summary>Claims one task using its displayed optimistic version.</summary>
    public Task<TaskBoardMutationOutcome> ClaimAsync(
        Guid taskId,
        long expectedVersion,
        TaskActor actor,
        CancellationToken cancellationToken) => this.MutateAsync(
            HttpMethod.Post, $"/task-boards/tasks/{taskId:D}/claim",
            new MutationRequest(expectedVersion, actor.ActorId, actor.Kind), cancellationToken);

    /// <summary>Changes one acceptance result using the owning task version.</summary>
    public Task<TaskBoardMutationOutcome> SetCriterionAsync(
        Guid criterionId,
        long expectedVersion,
        bool isSatisfied,
        TaskActor actor,
        CancellationToken cancellationToken) => this.MutateAsync(
            HttpMethod.Put, $"/task-boards/criteria/{criterionId:D}",
            new CriterionRequest(
                expectedVersion, isSatisfied, actor.ActorId, actor.Kind), cancellationToken);

    /// <summary>Completes a task through the runtime's acceptance gates.</summary>
    public Task<TaskBoardMutationOutcome> CompleteAsync(
        Guid taskId,
        long expectedVersion,
        TaskActor actor,
        CancellationToken cancellationToken) => this.MutateAsync(
            HttpMethod.Post, $"/task-boards/tasks/{taskId:D}/complete",
            new MutationRequest(expectedVersion, actor.ActorId, actor.Kind), cancellationToken);

    /// <summary>Blocks, reopens, or cancels one task.</summary>
    public Task<TaskBoardMutationOutcome> SetStatusAsync(
        Guid taskId,
        long expectedVersion,
        TaskBoardStatus status,
        string detail,
        TaskActor actor,
        CancellationToken cancellationToken) => this.MutateAsync(
            HttpMethod.Put, $"/task-boards/tasks/{taskId:D}/status",
            new StatusRequest(
                expectedVersion, status, detail, actor.ActorId, actor.Kind), cancellationToken);

    /// <summary>Reads bounded durable activity for one board.</summary>
    public async Task<IReadOnlyList<TaskBoardActivity>> ActivityAsync(
        Guid boardId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        return await this.httpClient.GetFromJsonAsync<TaskBoardActivity[]>(
            $"/task-boards/{boardId:D}/activity?limit={limit}",
            jsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>Submits one feature request and returns its stable board id.</summary>
    public async Task<Guid> PlanAsync(
        Guid requestId,
        string featureRequest,
        TaskActor actor,
        FeaturePlannerKind planner,
        PrivacyClass privacy,
        ExecutionOrigin origin,
        CancellationToken cancellationToken)
    {
        var body = new PlanningRequest(
            requestId, featureRequest, actor.ActorId, actor.Kind, planner, privacy, origin);
        using var response = await this.httpClient.PostAsJsonAsync(
            "/task-boards/plan", body, jsonOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PlanningResponse>(
            jsonOptions, cancellationToken).ConfigureAwait(false);
        return result?.BoardId
            ?? throw new InvalidOperationException("The planning response had no board id.");
    }

    /// <summary>
    /// Asks the runtime to work one task now. Advisory: it returns the trace the run left
    /// behind and what the run said, and changes nothing on the board but its own record.
    /// </summary>
    public async Task<TaskWorkReply> WorkAsync(
        Guid boardId,
        Guid taskId,
        TaskActor actor,
        FeaturePlannerKind planner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/task-boards/{boardId:D}/tasks/{taskId:D}/work")
        {
            Content = JsonContent.Create(
                new WorkRequest(actor.ActorId, actor.Kind, planner), null, jsonOptions),
        };
        using var response = await this.httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return await ReadWorkReplyAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// A runtime that predates this route answers 404 with an empty body, and parsing
    /// that as JSON produced "the input does not contain any JSON tokens" — a message
    /// that says nothing about what is actually wrong. Name the real cause instead.
    /// </remarks>
    private static async Task<TaskWorkReply> ReadWorkReplyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new TaskWorkReply(
                false, Guid.Empty, string.Empty,
                "this runtime has no work endpoint — dami-host is older than this client "
                + "and needs redeploying");
        }

        try
        {
            var body = await response.Content
                .ReadFromJsonAsync<TaskWorkReply>(jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return body ?? new TaskWorkReply(
                false, Guid.Empty, string.Empty, "no reply from the runtime");
        }
        catch (JsonException)
        {
            return new TaskWorkReply(
                false, Guid.Empty, string.Empty,
                $"the runtime answered {(int)response.StatusCode} with no usable body");
        }
    }

    private sealed record WorkRequest(
        string ActorId, TaskActorKind ActorKind, FeaturePlannerKind Planner);

    private async Task<TaskBoardMutationOutcome> MutateAsync(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, body.GetType(), null, jsonOptions),
        };
        using var response = await this.httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return TaskBoardMutationOutcome.Conflict;
        }

        response.EnsureSuccessStatusCode();
        return TaskBoardMutationOutcome.Updated;
    }

    private sealed record MutationRequest(
        long ExpectedVersion,
        string ActorId,
        TaskActorKind ActorKind);

    private sealed record CriterionRequest(
        long ExpectedVersion,
        bool IsSatisfied,
        string ActorId,
        TaskActorKind ActorKind);

    private sealed record StatusRequest(
        long ExpectedVersion,
        TaskBoardStatus Status,
        string Detail,
        string ActorId,
        TaskActorKind ActorKind);

    private sealed record PlanningRequest(
        Guid RequestId,
        string FeatureRequest,
        string ActorId,
        TaskActorKind ActorKind,
        FeaturePlannerKind Planner,
        PrivacyClass Privacy,
        ExecutionOrigin Origin);

    private sealed record PlanningResponse(Guid BoardId);
}
