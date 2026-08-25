using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Curation;

/// <summary>Rewrites imported transcript voice into knowledge Dami can actually use.</summary>
/// <remarks>
/// The Hermes import carried its own framing across unchanged — "As of 2026-03-02 the
/// user reports they are noticeably less afraid of dying" — so a third of the corpus
/// reads as minutes about a stranger, with the date restated in prose that the row
/// already stores as a column. Every retrieval and every belief formed from it inherits
/// that.
///
/// Exactly the mundane, structured work the local sidecar is for. The rewrite is
/// derived: the original observation is never touched, and a bad rewrite is undone by
/// deleting one row.
/// </remarks>
public sealed class CuratorService : IProactiveService
{
    private const string INSTRUCTIONS =
        """
        Rewrite this note as a direct statement of what is true about Steve, for his
        assistant's own memory. Rules:
        - Say "Steve", not "the user". Say "Dami" or "I", not "the assistant".
        - Drop date prefixes like "As of 2026-03-02," or "Summary:" — the date is stored
          separately. Keep dates that are part of the fact itself (an appointment, a
          diagnosis date).
        - Keep every fact, number, name and nuance. Losing detail is worse than leaving
          the note alone.
        - Do not add anything that is not in the note. Do not interpret or conclude.
        - One or two plain sentences. Output only the rewritten note.
        """;

    private readonly IObservationCurationStore curationStore;
    private readonly IChatClient chatClient;
    private readonly CuratorOptions curatorOptions;
    private readonly ILogger<CuratorService> logger;

    /// <summary>Creates the service.</summary>
    public CuratorService(
        IObservationCurationStore curationStore,
        IChatClient chatClient,
        IOptions<CuratorOptions> curatorOptions,
        ILogger<CuratorService> logger)
    {
        ArgumentNullException.ThrowIfNull(curationStore);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(curatorOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.curationStore = curationStore;
        this.chatClient = chatClient;
        this.curatorOptions = curatorOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "curator";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var pending = new List<Observation>();
        await foreach (var observation in this.curationStore
            .UncuratedAsync(this.curatorOptions.BatchSize, cancellationToken).ConfigureAwait(false))
        {
            pending.Add(observation);
        }

        var curated = 0;
        foreach (var observation in pending)
        {
            curated += await this.CurateOneAsync(observation, cancellationToken)
                .ConfigureAwait(false);
        }

        if (curated > 0)
        {
            this.logger.LogInformation(
                "Curator: rewrote {Count} of {Examined} observation(s)", curated, pending.Count);
        }

        return ProactiveResult.quiet;
    }

    private async Task<int> CurateOneAsync(
        Observation observation,
        CancellationToken cancellationToken)
    {
        try
        {
            var rewritten = (await this.chatClient
                .CompleteAsync($"{INSTRUCTIONS}\n\nNote: {observation.Body}", cancellationToken)
                .ConfigureAwait(false)).Trim();

            if (!IsAcceptable(rewritten, observation.Body))
            {
                return 0;
            }

            await this.curationStore
                .CurateAsync(observation.ObservationId, rewritten, cancellationToken)
                .ConfigureAwait(false);
            return 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(
                exception, "Curating {Observation} failed; leaving it alone",
                observation.ObservationId);
            return 0;
        }
    }

    /// <summary>
    /// Refuses a rewrite that lost the note. A curation that drops half the content is
    /// worse than the clumsy original, and the original is the thing beliefs were built
    /// from — so when in doubt, keep what was recorded.
    /// </summary>
    private static bool IsAcceptable(string rewritten, string original)
    {
        return rewritten.Length >= original.Length / 2
            && rewritten.Length <= original.Length * 2
            && !rewritten.Contains("the user", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>How much the curator rewrites per pass.</summary>
public sealed class CuratorOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Curator";

    /// <summary>Observations rewritten per pass.</summary>
    public int BatchSize { get; set; } = 60;
}
