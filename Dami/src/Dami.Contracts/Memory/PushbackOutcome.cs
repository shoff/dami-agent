namespace Dami.Contracts.Memory;

/// <summary>What happened after Dami pushed back.</summary>
public enum PushbackOutcome
{
    /// <summary>Steve agreed and changed course.</summary>
    Accepted = 0,

    /// <summary>Steve disagreed and proceeded.</summary>
    Rejected = 1,

    /// <summary>Acknowledged, not resolved either way yet.</summary>
    Deferred = 2,

    /// <summary>Recorded but never followed up. The default until something happens.</summary>
    Unresolved = 3,
}
