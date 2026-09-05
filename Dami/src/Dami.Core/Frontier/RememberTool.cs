using System.Text.Json;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Frontier;

/// <summary>Lets the frontier write one durable note into Steve's memory (ADR-0030).</summary>
public interface IFrontierRemember
{
    /// <summary>The tool as offered to the frontier.</summary>
    FrontierTool Tool { get; }

    /// <summary>Records the note and makes it findable now, not at the next nightly pass.</summary>
    Task<FrontierToolResult> RememberAsync(
        Guid traceId, string channel, string note, CancellationToken cancellationToken);
}

/// <summary>
/// "Remember that…" as an observation with provenance, embedded on the spot so a
/// <c>recall</c> five minutes later finds it. The nightly embedder would catch it
/// anyway; embedding here is what makes the pair feel like memory rather than a queue.
/// </summary>
public sealed class RememberTool : IFrontierRemember
{
    /// <summary>The tool's name on the wire.</summary>
    public const string NAME = "remember";

    /// <summary>The observation source, so the corpus says where a note came from.</summary>
    public const string SOURCE = "frontier-remember";

    private readonly IObservationCorpus corpus;
    private readonly IEmbeddingClient embeddings;
    private readonly IObservationEmbeddingStore embeddingStore;
    private readonly TimeProvider clock;
    private readonly ILogger<RememberTool> logger;

    /// <summary>Creates the tool.</summary>
    public RememberTool(
        IObservationCorpus corpus,
        IEmbeddingClient embeddings,
        IObservationEmbeddingStore embeddingStore,
        TimeProvider clock,
        ILogger<RememberTool> logger)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.corpus = corpus;
        this.embeddings = embeddings;
        this.embeddingStore = embeddingStore;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public FrontierTool Tool { get; } = new(
        NAME,
        "Save one fact Steve wants kept: something he told you to remember, a preference, a "
        + "plan, a person, a number. Write it as a complete standalone sentence in the third "
        + "person (\"Steve's dentist is Dr. Park; next visit 2026-10-02\"). Use it when he says "
        + "remember, note, don't forget, or clearly states a fact about himself worth keeping.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { note = new { type = "string", description = "The fact, as one standalone sentence." } },
            required = new[] { "note" },
            additionalProperties = false,
        }));

    /// <inheritdoc />
    public async Task<FrontierToolResult> RememberAsync(
        Guid traceId, string channel, string note, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        var observation = new Observation(
            Guid.NewGuid(), this.clock.GetUtcNow(), SOURCE, note.Trim(),
            new Dictionary<string, string> { ["trace"] = traceId.ToString("N"), ["channel"] = channel });
        await this.corpus.RecordAsync(observation, cancellationToken).ConfigureAwait(false);

        try
        {
            var vectors = await this.embeddings.EmbedAsync([observation.Body], cancellationToken)
                .ConfigureAwait(false);
            await this.embeddingStore.StoreAsync(
                observation.ObservationId, this.embeddings.ModelId, vectors[0], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Saved, just not searchable until the nightly pass. Say so rather than fail.
            this.logger.LogWarning(exception, "Remembered {Id} but could not embed it yet", observation.ObservationId);
            return FrontierToolResult.Ok("Saved. It will be searchable after tonight's indexing pass.");
        }

        this.logger.LogInformation("Remembered {Id} from {Channel}", observation.ObservationId, channel);
        return FrontierToolResult.Ok("Saved and searchable.");
    }
}
