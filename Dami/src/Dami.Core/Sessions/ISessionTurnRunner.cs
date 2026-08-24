using Dami.Contracts.Sessions;

namespace Dami.Core.Sessions;

/// <summary>Executes idempotent durable requests inside a conversation session.</summary>
public interface ISessionTurnRunner
{
    /// <summary>Runs or replays one client-identified request.</summary>
    Task<SessionTurnOutcome> RunAsync(
        ConversationTurnRequest request,
        CancellationToken cancellationToken);
}
