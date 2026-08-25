namespace Dami.Contracts.TaskBoard;

/// <summary>Whether work is waiting, active, blocked, or terminal.</summary>
public enum TaskBoardStatus
{
    /// <summary>Available once its prerequisites are complete.</summary>
    Open,

    /// <summary>Claimed and actively being worked.</summary>
    InProgress,

    /// <summary>Unable to proceed until an external condition changes.</summary>
    Blocked,

    /// <summary>All acceptance criteria and child work are complete.</summary>
    Done,

    /// <summary>Deliberately removed from the plan.</summary>
    Cancelled,
}

/// <summary>How a set of sibling tasks is presented.</summary>
public enum TaskOrdering
{
    /// <summary>The explicit position is consequential.</summary>
    Ordered,

    /// <summary>Higher priority is shown first; position is the stable tie-break.</summary>
    Priority,
}

/// <summary>Relative urgency when sibling order is not consequential.</summary>
public enum TaskPriority
{
    /// <summary>Can wait.</summary>
    Low,

    /// <summary>Normal planned work.</summary>
    Normal,

    /// <summary>Should precede normal work.</summary>
    High,

    /// <summary>Must be handled before lower-priority work.</summary>
    Critical,
}

/// <summary>Who created or claimed work.</summary>
public enum TaskActorKind
{
    /// <summary>A person.</summary>
    Human,

    /// <summary>An autonomous or interactive software agent.</summary>
    Agent,
}
