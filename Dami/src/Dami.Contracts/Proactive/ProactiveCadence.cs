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
}
