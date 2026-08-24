using Dami.Contracts.Sessions;

namespace Dami.Core.Sessions;

/// <summary>The durable turn returned after execution or an idempotent reconnect.</summary>
public sealed record SessionTurnOutcome
{
    /// <summary>Creates an outcome.</summary>
    public SessionTurnOutcome(ConversationTurn turn, bool wasReplay)
    {
        ArgumentNullException.ThrowIfNull(turn);
        this.Turn = turn;
        this.WasReplay = wasReplay;
    }

    /// <summary>The current durable turn state.</summary>
    public ConversationTurn Turn { get; }

    /// <summary>Whether the request ID already existed before this call.</summary>
    public bool WasReplay { get; }
}
