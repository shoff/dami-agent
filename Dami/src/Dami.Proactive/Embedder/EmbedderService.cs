using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Embedder;

/// <summary>Keeps the corpus's semantic index current (ADR-0009).</summary>
/// <remarks>
/// A proactive service rather than a hook on the corpus store, deliberately: the stores
/// stay dumb and the coupling to the inference sidecar lives in the tier that is allowed
/// to be slow. Idempotent — a re-run embeds only what lacks a vector under the
/// configured model, which is also exactly the re-embedding path a future model change
/// needs. Produces no conclusions and no surfacings; index maintenance is not worth
/// anyone's attention.
/// </remarks>
public sealed class EmbedderService : IProactiveService
{
    private const int BATCH = 32;

    private readonly IObservationEmbeddingStore embeddingStore;
    private readonly IConclusionEmbeddingStore conclusionEmbeddingStore;
    private readonly IEmbeddingClient embeddingClient;
    private readonly EmbedderOptions embedderOptions;
    private readonly ILogger<EmbedderService> logger;

    /// <summary>Creates the service.</summary>
    public EmbedderService(
        IObservationEmbeddingStore embeddingStore,
        IConclusionEmbeddingStore conclusionEmbeddingStore,
        IEmbeddingClient embeddingClient,
        IOptions<EmbedderOptions> embedderOptions,
        ILogger<EmbedderService> logger)
    {
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(conclusionEmbeddingStore);
        ArgumentNullException.ThrowIfNull(embeddingClient);
        ArgumentNullException.ThrowIfNull(embedderOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.embeddingStore = embeddingStore;
        this.conclusionEmbeddingStore = conclusionEmbeddingStore;
        this.embeddingClient = embeddingClient;
        this.embedderOptions = embedderOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "embedder";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var embedded = 0;

        while (embedded < this.embedderOptions.MaxPerPass)
        {
            var pending = await this.CollectPendingAsync(cancellationToken).ConfigureAwait(false);
            if (pending.Count == 0)
            {
                break;
            }

            await this.EmbedBatchAsync(pending, cancellationToken).ConfigureAwait(false);
            embedded += pending.Count;
        }

        var beliefsIndexed = await this.EmbedBeliefsAsync(cancellationToken).ConfigureAwait(false);

        if (embedded > 0 || beliefsIndexed > 0)
        {
            this.logger.LogInformation(
                "Embedder: {Count} observation(s) and {Beliefs} belief(s) indexed under {Model}",
                embedded, beliefsIndexed, this.embeddingClient.ModelId);
        }

        return ProactiveResult.quiet;
    }

    /// <summary>Indexes active beliefs lacking a vector (D-009: only the active set).</summary>
    private async Task<int> EmbedBeliefsAsync(CancellationToken cancellationToken)
    {
        var pending = new List<Conclusion>();
        await foreach (var conclusion in this.conclusionEmbeddingStore
            .UnembeddedAsync(this.embeddingClient.ModelId, BATCH, cancellationToken)
            .ConfigureAwait(false))
        {
            pending.Add(conclusion);
        }

        if (pending.Count == 0)
        {
            return 0;
        }

        var texts = new List<string>(pending.Count);
        foreach (var conclusion in pending)
        {
            texts.Add(conclusion.Statement);
        }

        var vectors = await this.embeddingClient.EmbedAsync(texts, cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < pending.Count; index++)
        {
            await this.conclusionEmbeddingStore.StoreAsync(
                pending[index].ConclusionId, this.embeddingClient.ModelId,
                vectors[index], cancellationToken).ConfigureAwait(false);
        }

        return pending.Count;
    }

    private async Task<List<Observation>> CollectPendingAsync(CancellationToken cancellationToken)
    {
        var pending = new List<Observation>();
        await foreach (var observation in this.embeddingStore
            .UnembeddedAsync(this.embeddingClient.ModelId, BATCH, cancellationToken)
            .ConfigureAwait(false))
        {
            pending.Add(observation);
        }

        return pending;
    }

    private async Task EmbedBatchAsync(List<Observation> pending, CancellationToken cancellationToken)
    {
        var texts = new List<string>(pending.Count);
        foreach (var observation in pending)
        {
            texts.Add(observation.Body);
        }

        var vectors = await this.embeddingClient.EmbedAsync(texts, cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < pending.Count; index++)
        {
            await this.embeddingStore.StoreAsync(
                pending[index].ObservationId,
                this.embeddingClient.ModelId,
                vectors[index],
                cancellationToken).ConfigureAwait(false);
        }
    }
}
