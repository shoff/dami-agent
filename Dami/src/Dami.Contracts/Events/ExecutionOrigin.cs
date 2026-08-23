namespace Dami.Contracts.Events;

/// <summary>What caused a trace to exist.</summary>
/// <remarks>
/// D-018. The charter's event contract assumed a user turn; most of this system's work
/// has no user attached. Without this discriminator that work is invisible to the
/// execution graph, which defeats the graph.
/// </remarks>
public enum ExecutionOrigin
{
    /// <summary>A person asked for something and is waiting.</summary>
    UserTurn = 0,

    /// <summary>A proactive service running on its cadence, with nobody present.</summary>
    ScheduledService = 1,

    /// <summary>Work triggered by an observed change rather than a clock or a request.</summary>
    ReactiveTrigger = 2,

    /// <summary>Dami examining its own behaviour — the pushback review of D-011.</summary>
    SelfAudit = 3,
}
