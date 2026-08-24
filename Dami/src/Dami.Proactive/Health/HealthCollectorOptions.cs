namespace Dami.Proactive.Health;

/// <summary>How much the health collector reads per pass.</summary>
public sealed class HealthCollectorOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "HealthCollector";

    /// <summary>Observations examined per pass.</summary>
    public int BatchSize { get; set; } = 40;
}
