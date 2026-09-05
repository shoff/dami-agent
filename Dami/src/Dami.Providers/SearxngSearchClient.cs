using System.Text.Json;
using Dami.Contracts.Research;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>Asks the private SearXNG for its JSON results.</summary>
/// <remarks>
/// SearXNG runs on this host and does the egress to the engines itself, so from the
/// runtime's side this is a loopback call — but the query still leaves the machine in
/// SearXNG's requests, which is why the tool gates it before it gets here.
/// </remarks>
public sealed class SearxngSearchClient : ISearchEngine
{
    private readonly HttpClient httpClient;
    private readonly SearxngOptions searxngOptions;
    private readonly ILogger<SearxngSearchClient> logger;

    /// <summary>Creates the client.</summary>
    public SearxngSearchClient(HttpClient httpClient, IOptions<SearxngOptions> searxngOptions, ILogger<SearxngSearchClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(searxngOptions);
        ArgumentNullException.ThrowIfNull(logger);
        this.httpClient = httpClient;
        this.searxngOptions = searxngOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);
        var endpoint = new Uri(new Uri(this.searxngOptions.BaseUrl), "/search?format=json&language=en&q=" + Uri.EscapeDataString(query.Trim()));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(this.searxngOptions.TimeoutSeconds));
        using var response = await this.httpClient.GetAsync(endpoint, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        var results = Parse(body.RootElement).Take(limit).ToList();
        this.logger.LogInformation("SearXNG answered {Count} result(s) for a {Length}-char query", results.Count, query.Length);
        return results;
    }

    /// <summary>The installed SearXNG's JSON shape: results[] with title, url, content, engines[].</summary>
    public static IEnumerable<SearchResult> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in results.EnumerateArray())
        {
            var url = item.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            {
                continue;
            }

            var engines = item.TryGetProperty("engines", out var enginesElement) && enginesElement.ValueKind == JsonValueKind.Array
                ? string.Join(",", enginesElement.EnumerateArray().Select(engine => engine.GetString()))
                : string.Empty;
            yield return new SearchResult(
                Text(item, "title"), parsed, Text(item, "content"), engines);
        }
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;
}
