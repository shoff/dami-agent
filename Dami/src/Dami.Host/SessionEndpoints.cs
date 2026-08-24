using Dami.Contracts.Sessions;
using Dami.Core.Sessions;

namespace Dami.Host;

/// <summary>Durable conversation lifecycle and reconnect routes.</summary>
public static class SessionEndpoints
{
    private const int DEFAULT_LIST_LIMIT = 20;
    private const int MAX_LIST_LIMIT = 100;

    /// <summary>Maps session routes.</summary>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/sessions", StartAsync);
        app.MapGet("/sessions", ListAsync);
        app.MapGet("/sessions/{sessionId:guid}", FindAsync);
        app.MapPost("/sessions/{sessionId:guid}/resume", ResumeAsync);
        app.MapPost("/sessions/{sessionId:guid}/interrupt", InterruptAsync);
        app.MapPost("/sessions/{sessionId:guid}/turns", RunTurnAsync);
        app.MapGet("/sessions/{sessionId:guid}/turns/{requestId:guid}", FindTurnAsync);
    }

    private static async Task<IResult> StartAsync(
        StartSessionRequest request,
        IConversationSessionManager manager,
        CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "sessionId must be non-empty" });
        }

        var session = await manager
            .StartAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
        return Results.Created($"/sessions/{session.SessionId:D}", session);
    }

    private static async Task<IResult> ListAsync(
        IConversationSessionManager manager,
        int? limit,
        CancellationToken cancellationToken)
    {
        var requested = limit ?? DEFAULT_LIST_LIMIT;
        if (requested is < 1 or > MAX_LIST_LIMIT)
        {
            return Results.BadRequest(new { error = $"limit must be 1-{MAX_LIST_LIMIT}" });
        }

        var sessions = new List<ConversationSession>(requested);
        await foreach (var session in manager.ListRecentAsync(requested, cancellationToken)
            .ConfigureAwait(false))
        {
            sessions.Add(session);
        }

        return Results.Ok(sessions);
    }

    private static async Task<IResult> FindAsync(
        Guid sessionId,
        IConversationSessionManager manager,
        CancellationToken cancellationToken)
    {
        var session = await manager
            .FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }

    private static async Task<IResult> ResumeAsync(
        Guid sessionId,
        IConversationSessionManager manager,
        CancellationToken cancellationToken)
    {
        var session = await manager
            .ResumeAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }

    private static async Task<IResult> InterruptAsync(
        Guid sessionId,
        IConversationSessionManager manager,
        CancellationToken cancellationToken)
    {
        var session = await manager
            .InterruptAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }

    private static async Task<IResult> RunTurnAsync(
        Guid sessionId,
        RunSessionTurnRequest request,
        ISessionTurnRunner runner,
        [FromKeyedServices("frontier")] ISessionTurnRunner frontierRunner,
        IConversationTurnStore turnStore,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || request.RequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(
                new { error = "sessionId, requestId, and message are required" });
        }

        var turnRequest = new ConversationTurnRequest(
            sessionId, request.RequestId, request.Message, clock.GetUtcNow());
        SessionTurnOutcome outcome;
        try
        {
            // Same session, same journal; the flag chooses which model answers this turn.
            outcome = await (request.Frontier ? frontierRunner : runner)
                .RunAsync(turnRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var interrupted = await turnStore.FindTurnAsync(
                sessionId, request.RequestId, CancellationToken.None).ConfigureAwait(false);
            if (interrupted?.State != ConversationTurnState.Interrupted)
            {
                throw;
            }

            outcome = new SessionTurnOutcome(interrupted, wasReplay: false);
        }

        return Results.Ok(outcome);
    }

    private static async Task<IResult> FindTurnAsync(
        Guid sessionId,
        Guid requestId,
        IConversationTurnStore turnStore,
        CancellationToken cancellationToken)
    {
        var turn = await turnStore
            .FindTurnAsync(sessionId, requestId, cancellationToken).ConfigureAwait(false);
        return turn is null ? Results.NotFound() : Results.Ok(turn);
    }
}

/// <summary>Starts a durable session with a stable client-generated identifier.</summary>
public sealed record StartSessionRequest(Guid SessionId);

/// <summary>Runs an idempotent session turn with a client-generated retry key.</summary>
public sealed record RunSessionTurnRequest(Guid RequestId, string Message, bool Frontier = false);
