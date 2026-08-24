namespace Dami.Contracts.Sessions;

/// <summary>The durable state of one request within a conversation session.</summary>
public enum ConversationTurnState
{
    /// <summary>The request was reserved and execution has not reached a terminal state.</summary>
    Running = 0,

    /// <summary>The complete assistant response is durable.</summary>
    Completed = 1,

    /// <summary>Execution was deliberately interrupted.</summary>
    Interrupted = 2,

    /// <summary>Execution failed.</summary>
    Failed = 3,
}
