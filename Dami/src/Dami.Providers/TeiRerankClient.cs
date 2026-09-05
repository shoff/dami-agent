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
    /// <inheritdoc />
    /// <remarks>
    /// TEI refuses more candidates than its <c>--max-client-batch-size</c> (32 by default)
    /// with a 422; on 2026-09-05 the opportunity scout sent forty and lost its ranking.
    /// Larger sets go in batches and the raw scores are merged before sorting.
    /// </remarks>
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

        var batch = Math.Max(1, this.rerankOptions.MaxBatch);
        var ranked = new List<(int Index, double Score)>(candidates.Count);
        for (var start = 0; start < candidates.Count; start += batch)
        {
            var slice = candidates.Skip(start).Take(batch).ToList();
            var scored = await this.ScoreAsync(query, slice, cancellationToken).ConfigureAwait(false);
            ranked.AddRange(scored.Select(item => (start + item.Index, item.Score)));
        }

        ranked.Sort((left, right) => right.Score.CompareTo(left.Score));
        return ranked.Select(item => item.Index).ToList();
    }

    /// <summary>One TEI request: raw cross-encoder scores for a slice no larger than the batch limit.</summary>
    private async Task<List<(int Index, double Score)>> ScoreAsync(
        string query, IReadOnlyList<string> slice, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(this.baseUri, "/rerank");
        using var response = await this.httpClient
            .PostAsJsonAsync(endpoint, new { query, texts = slice, raw_scores = true }, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return Scores(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), slice.Count);
    }

    private static List<(int Index, double Score)> Scores(string json, int candidateCount)
    {
        using var body = JsonDocument.Parse(json);
        var scored = new List<(int Index, double Score)>();
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

            scored.Add((index, item.GetProperty("score").GetDouble()));
        }

        return scored;
    }
}
