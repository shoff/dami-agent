namespace Dami.Privacy;

/// <summary>Bounds on egress attempt rate (C5).</summary>
public sealed class EgressBudgetOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "EgressBudget";

    /// <summary>Most egress attempts allowed in any rolling hour.</summary>
    public int MaxPerHour { get; set; } = 30;

    /// <summary>Most egress attempts allowed in any rolling day.</summary>
    public int MaxPerDay { get; set; } = 200;
}
