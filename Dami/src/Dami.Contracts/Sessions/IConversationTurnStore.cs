namespace Dami.Contracts.Sessions;

/// <summary>Idempotent conversation-turn journal.</summary>
public interface IConversationTurnStore
{
    /// <summary>Reserves a request or returns its existing exact reservation.</summary>
    Task<ConversationTurnReservation> ReserveTurnAsync(
        ConversationTurnRequest request,
        CancellationToken cancellationToken);

    /// <summary>Persists the response only if the turn is still Running.</summary>
    Task<bool> CompleteTurnAsync(
        Guid sessionId,
        Guid requestId,
        string response,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    /// <summary>Marks a Running turn Interrupted; a later completion cannot win.</summary>
    Task<bool> InterruptTurnAsync(
        Guid sessionId,
        Guid requestId,
        DateTimeOffset interruptedAt,
        CancellationToken cancellationToken);

    /// <summary>Marks a Running turn Failed without persisting error text as conversation.</summary>
    Task<bool> FailTurnAsync(
        Guid sessionId,
        Guid requestId,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);

    /// <summary>Finds one request in its session.</summary>
    Task<ConversationTurn?> FindTurnAsync(
        Guid sessionId,
        Guid requestId,
        CancellationToken cancellationToken);

    /// <summary>Reads the newest completed turns, ordered oldest-to-newest for prompting.</summary>
    IAsyncEnumerable<ConversationTurn> RecentCompletedTurnsAsync(
        Guid sessionId,
        int limit,
        CancellationToken cancellationToken);
}
