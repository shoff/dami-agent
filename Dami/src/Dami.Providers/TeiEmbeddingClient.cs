using System.Net.Http.Json;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>The TEI sidecar as an <see cref="IEmbeddingClient"/>.</summary>
/// <remarks>
/// Talks to loopback only. This is deliberately NOT an egress client and must never be
/// wrapped in one: text embedded here may be profile-derived, and it stays on the host —
/// that separation is the D-012 boundary doing its job.
/// </remarks>
public sealed class TeiEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient httpClient;
    private readonly TeiOptions teiOptions;
    private readonly ILogger<TeiEmbeddingClient> logger;

    /// <summary>Creates the client.</summary>
    public TeiEmbeddingClient(
        HttpClient httpClient,
        IOptions<TeiOptions> teiOptions,
        ILogger<TeiEmbeddingClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(teiOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.teiOptions = teiOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var vectors = new List<float[]>(texts.Count);
        var endpoint = new Uri(new Uri(this.teiOptions.BaseUrl), "/embed");

        for (var start = 0; start < texts.Count; start += this.teiOptions.BatchSize)
        {
            var count = Math.Min(this.teiOptions.BatchSize, texts.Count - start);
            var chunk = new string[count];
            for (var index = 0; index < count; index++)
            {
                chunk[index] = texts[start + index];
            }

            using var response = await this.httpClient
                .PostAsJsonAsync(endpoint, new { inputs = chunk }, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var embedded = await response.Content
                .ReadFromJsonAsync<float[][]>(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("TEI returned an empty embedding response.");

            vectors.AddRange(embedded);
        }

        return vectors;
    }
}
