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
    private readonly Uri baseUri;
    private readonly TeiOptions teiOptions;
    private readonly ILogger<TeiEmbeddingClient> logger;

    /// <inheritdoc />
    public string ModelId => this.teiOptions.ModelId;

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
        this.baseUri = LocalSidecarEndpoint.Parse(this.teiOptions.BaseUrl, nameof(teiOptions));

        if (this.teiOptions.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(teiOptions),
                this.teiOptions.BatchSize,
                "TEI batch size must be positive.");
        }

        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var vectors = new List<float[]>(texts.Count);
        var endpoint = new Uri(this.baseUri, "/embed");
        int? dimensions = null;

        for (var start = 0; start < texts.Count; start += this.teiOptions.BatchSize)
        {
            var count = Math.Min(this.teiOptions.BatchSize, texts.Count - start);
            var chunk = new string[count];
            for (var index = 0; index < count; index++)
            {
                chunk[index] = texts[start + index];
            }

            var embedded = await this.EmbedChunkAsync(endpoint, chunk, cancellationToken).ConfigureAwait(false);
            dimensions = ValidateDimensions(embedded, dimensions);
            vectors.AddRange(embedded);
        }

        return vectors;
    }

    private async Task<float[][]> EmbedChunkAsync(
        Uri endpoint,
        string[] chunk,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient
            .PostAsJsonAsync(endpoint, new { inputs = chunk }, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var embedded = await response.Content
            .ReadFromJsonAsync<float[][]>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("TEI returned an empty embedding response.");

        if (embedded.Length != chunk.Length)
        {
            throw new InvalidDataException(
                $"TEI returned {embedded.Length} vectors for a batch of {chunk.Length} texts.");
        }

        return embedded;
    }

    private static int ValidateDimensions(float[][] vectors, int? expectedDimensions)
    {
        var dimensions = expectedDimensions ?? vectors[0].Length;

        foreach (var vector in vectors)
        {
            if (vector.Length == 0 || vector.Length != dimensions)
            {
                throw new InvalidDataException("TEI returned inconsistent embedding dimensions.");
            }
        }

        return dimensions;
    }
}
