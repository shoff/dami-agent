namespace Dami.Core.Context;

/// <summary>Tunables for local query planning.</summary>
public sealed class QueryPlanOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "QueryPlan";

    /// <summary>Whether to plan at all. Off means search the request verbatim, as before.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Most searches to run for one request.</summary>
    public int MaxSearches { get; set; } = 4;

    /// <summary>Slots kept per search, before the union is reranked as a whole.</summary>
    public int SlotsPerSearch { get; set; } = 6;

    /// <summary>Structured facts taken from each named domain, for grounding and for context.</summary>
    public int FactsPerDomain { get; set; } = 8;

    /// <summary>Domains a plan may name. Anything else the model invents is dropped.</summary>
    public HashSet<string> KnownDomains { get; } = new(StringComparer.Ordinal) { "health" };
}
