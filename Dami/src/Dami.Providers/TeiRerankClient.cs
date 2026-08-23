using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>The TEI reranker sidecar as an <see cref="IRerankClient"/>.</summary>
public sealed class TeiRerankClient : IRerankClient
{
    private readonly HttpClient httpClient;
    private readonly TeiRerankOptions rerankOptions;
    private readonly ILogger<TeiRerankClient> logger;

    /// <summary>Creates the client.</summary>
    public TeiRerankClient(
        HttpClient httpClient,
        IOptions<TeiRerankOptions> rerankOptions,
        ILogger<TeiRerankClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(rerankOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.rerankOptions = rerankOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> RankAsync(
        string query,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return [];
        }

        var endpoint = new Uri(new Uri(this.rerankOptions.BaseUrl), "/rerank");
        using var response = await this.httpClient
            .PostAsJsonAsync(endpoint, new { query, texts = candidates, raw_scores = true }, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return Order(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private static List<int> Order(string json)
    {
        using var body = JsonDocument.Parse(json);

        var ranked = new List<(int Index, double Score)>();
        foreach (var item in body.RootElement.EnumerateArray())
        {
            ranked.Add((item.GetProperty("index").GetInt32(), item.GetProperty("score").GetDouble()));
        }

        ranked.Sort((left, right) => right.Score.CompareTo(left.Score));

        var order = new List<int>(ranked.Count);
        foreach (var (index, _) in ranked)
        {
            order.Add(index);
        }

        return order;
    }
}
