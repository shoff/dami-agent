using Dami.Contracts.Events;

namespace Dami.Contracts.Research;

/// <summary>One web search hit.</summary>
public sealed record SearchResult(string Title, Uri Url, string Snippet, string Engine);

/// <summary>A search engine the runtime may ask. The query leaves the host; gate it first.</summary>
public interface ISearchEngine
{
    /// <summary>The best hits for a query, best first.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}

/// <summary>A public page, read for research: its text, not its markup.</summary>
public sealed record ResearchPage(Uri Url, int StatusCode, string Title, string Text);

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
