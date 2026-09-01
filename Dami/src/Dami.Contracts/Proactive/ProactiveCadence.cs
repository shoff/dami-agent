namespace Dami.Contracts.Proactive;

/// <summary>How often a proactive service runs.</summary>
public enum ProactiveCadence
{
    /// <summary>Once a night — the interest scout's cadence.</summary>
    Nightly = 0,

    /// <summary>Once a week — the reflection pass. One observation, Sunday night, or nothing.</summary>
    Weekly = 1,

    /// <summary>Once a quarter — the pushback review of D-011.</summary>
    Quarterly = 2,

    /// <summary>
    /// Every eight hours — three passes a day, which is what a morning/midday/evening
    /// job needs (ADR-0027). The scheduler is interval-based and has no notion of clock
    /// time, so a service wanting a time of day reads it from the clock when it runs.
    /// </summary>
    EightHourly = 3,
}
