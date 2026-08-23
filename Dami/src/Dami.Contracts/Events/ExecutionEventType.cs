namespace Dami.Contracts.Events;

/// <summary>The kinds of operation the execution stream records.</summary>
/// <remarks>
/// The list from dami-core-charter.md §7.2. Persisted as text and deliberately not
/// constrained in the database, because these will grow and a check constraint would
/// make every new type a migration.
/// </remarks>
public enum ExecutionEventType
{
    /// <summary>A trace was accepted but has not begun.</summary>
    TraceQueued = 0,

    /// <summary>A trace began.</summary>
    TraceStarted = 1,

    /// <summary>Retrieval of relevant context began.</summary>
    ContextRetrievalStarted = 2,

    /// <summary>Context was retrieved, with provenance.</summary>
    ContextRetrieved = 3,

    /// <summary>A capability bundle was selected for this trace.</summary>
    CapabilitySelected = 4,

    /// <summary>A worker or sub-agent was created.</summary>
    AgentSpawned = 5,

    /// <summary>A worker reported progress.</summary>
    AgentProgressed = 6,

    /// <summary>A worker finished and returned evidence.</summary>
    AgentCompleted = 7,

    /// <summary>The model asked for a tool.</summary>
    ToolRequested = 8,

    /// <summary>Tool execution began.</summary>
    ToolStarted = 9,

    /// <summary>Tool execution finished successfully.</summary>
    ToolCompleted = 10,

    /// <summary>Tool execution failed.</summary>
    ToolFailed = 11,

    /// <summary>A consequential action is blocked pending explicit approval.</summary>
    ApprovalRequested = 12,

    /// <summary>An approval was granted or refused.</summary>
    ApprovalResolved = 13,

    /// <summary>Dami asked the user to disambiguate.</summary>
    ClarificationRequested = 14,

    /// <summary>The user answered a clarification.</summary>
    ClarificationResolved = 15,

    /// <summary>An artifact was produced and stored by reference.</summary>
    ArtifactProduced = 16,

    /// <summary>Response tokens are streaming; coalesced, never one event per token.</summary>
    ResponseStreaming = 17,

    /// <summary>A trace finished successfully.</summary>
    TraceCompleted = 18,

    /// <summary>A trace failed.</summary>
    TraceFailed = 19,

    /// <summary>A trace was cancelled.</summary>
    TraceCancelled = 20,

    /// <summary>A proactive pass concluded something without surfacing it (D-021).</summary>
    ConclusionRecorded = 21,

    /// <summary>A proactive pass cleared the bar and said something unprompted.</summary>
    Surfaced = 22,
}
