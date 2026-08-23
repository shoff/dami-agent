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
    private readonly Uri baseUri;
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
        this.baseUri = LocalSidecarEndpoint.Parse(this.rerankOptions.BaseUrl, nameof(rerankOptions));
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

        var endpoint = new Uri(this.baseUri, "/rerank");
        using var response = await this.httpClient
            .PostAsJsonAsync(endpoint, new { query, texts = candidates, raw_scores = true }, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return Order(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
            candidates.Count);
    }

    private static List<int> Order(string json, int candidateCount)
    {
        using var body = JsonDocument.Parse(json);

        var ranked = new List<(int Index, double Score)>();
        var seen = new HashSet<int>();
        foreach (var item in body.RootElement.EnumerateArray())
        {
            var index = item.GetProperty("index").GetInt32();
            if ((uint)index >= (uint)candidateCount)
            {
                throw new InvalidDataException($"Reranker returned out-of-range index {index}.");
            }

            if (!seen.Add(index))
            {
                throw new InvalidDataException($"Reranker returned duplicate index {index}.");
            }

            ranked.Add((index, item.GetProperty("score").GetDouble()));
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
