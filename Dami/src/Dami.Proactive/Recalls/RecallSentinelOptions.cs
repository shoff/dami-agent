namespace Dami.Proactive.Recalls;

/// <summary>The recall sentinel's sources and match terms (H12).</summary>
/// <remarks>
/// The URLs carry a date window and nothing else — the whole privacy design is that no
/// query ever says why Steve cares. Watch and household terms are configuration, not
/// health data; medication names come from the health domain at runtime, inside the
/// local-only matcher.
/// </remarks>
public sealed class RecallSentinelOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "RecallSentinel";

    /// <summary>openFDA drug enforcement; {0}/{1} are yyyyMMdd bounds.</summary>
    public string DrugUrl { get; set; } =
        "https://api.fda.gov/drug/enforcement.json?search=report_date:[{0}+TO+{1}]&limit=100";

    /// <summary>openFDA device enforcement; {0}/{1} are yyyyMMdd bounds.</summary>
    public string DeviceUrl { get; set; } =
        "https://api.fda.gov/device/enforcement.json?search=report_date:[{0}+TO+{1}]&limit=100";

    /// <summary>CPSC recalls; {0}/{1} are yyyy-MM-dd bounds.</summary>
    public string CpscUrl { get; set; } =
        "https://www.saferproducts.gov/RestWebServices/Recall?format=json&RecallDateStart={0}&RecallDateEnd={1}";

    /// <summary>How far back each pass looks.</summary>
    public int LookbackDays { get; set; } = 30;

    /// <summary>Non-medication concerns the matcher always watches for.</summary>
    public IList<string> WatchTerms { get; } =
        ["aortic valve", "heart valve", "mechanical valve"];

    /// <summary>Workshop and household gear worth watching CPSC for. Empty until named.</summary>
    public IList<string> HouseholdTerms { get; } = [];

    /// <summary>Confidence carried by each surfacing.</summary>
    public double Confidence { get; set; } = 0.8;
}
