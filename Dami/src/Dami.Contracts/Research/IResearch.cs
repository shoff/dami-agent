using Dami.Contracts.Events;

namespace Dami.Contracts.Research;

/// <summary>One web search hit.</summary>
public sealed record SearchResult(string Title, Uri Url, string Snippet, string Engine);

/// <summary>The kind of public resource a page referenced.</summary>
public enum ResearchReferenceKind
{
    /// <summary>A human-readable page.</summary>
    Page,

    /// <summary>A machine-readable API endpoint.</summary>
    Api,

    /// <summary>A machine-readable dataset or document.</summary>
    Data,

    /// <summary>An RSS, Atom, or similar syndication feed.</summary>
    Feed,
}

/// <summary>A typed outbound reference preserved from encountered content.</summary>
public sealed record ResearchReference(Uri Url, string Label, ResearchReferenceKind Kind);

/// <summary>A search engine the runtime may ask. The query leaves the host; gate it first.</summary>
public interface ISearchEngine
{
    /// <summary>The best hits for a query, best first.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}

/// <summary>A public page, read for research: its text, not its markup.</summary>
public sealed record ResearchPage(Uri Url, int StatusCode, string Title, string Text)
{
    /// <summary>Public-resource candidates referenced by this page.</summary>
    public IReadOnlyList<ResearchReference> References { get; init; } = [];
}

/// <summary>
/// Reads public pages for research (ADR-0033). An egress seam: read-only GET to public
/// hosts, never private ones, every fetch metered and recorded, nothing about the
/// profile in the request but the address itself.
/// </summary>
public interface IResearchReader
{
    /// <summary>Fetches one page and returns its readable text, capped.</summary>
    Task<ResearchPage> ReadAsync(Uri url, Guid traceId, ExecutionOrigin origin, CancellationToken cancellationToken);
}

/// <summary>One successfully read page and the references traversed to reach it.</summary>
public sealed record ResearchFinding(ResearchPage Page, int Depth, IReadOnlyList<Uri> Path);

/// <summary>The bounded result of following references from one seed.</summary>
public sealed record DeepResearchResult(
    IReadOnlyList<ResearchFinding> Findings,
    int PagesAttempted,
    int ReferencesConsidered,
    bool PageLimitReached)
{
    /// <summary>Failed reads retained so the answer can explain missing evidence.</summary>
    public IReadOnlyList<ResearchIssue> Issues { get; init; } = [];
}

/// <summary>Follows useful references from content already encountered.</summary>
public interface IDeepResearchService
{
    /// <summary>Explores several starting sites under one shared traversal budget.</summary>
    Task<DeepResearchResult> ExploreAsync(
        IReadOnlyList<Uri> seeds,
        string question,
        Guid traceId,
        ExecutionOrigin origin,
        CancellationToken cancellationToken);

    /// <summary>Explores one public seed for material relevant to the question.</summary>
    Task<DeepResearchResult> ExploreAsync(
        Uri seed,
        string question,
        Guid traceId,
        ExecutionOrigin origin,
        CancellationToken cancellationToken);
}
