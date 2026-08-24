using Dami.Core.Sessions;

namespace Dami.Core.Turns;

/// <summary>Executes a turn on a trace already reserved by a durable session.</summary>
public interface ITracedTurnRunner
{
    /// <summary>Runs with bounded recent conversation and the caller's stable trace.</summary>
    Task<TurnResult> RunTracedAsync(
        Guid traceId,
        string request,
        ConversationWindow conversation,
        CancellationToken cancellationToken);
}
