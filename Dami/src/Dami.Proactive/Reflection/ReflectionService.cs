using System.Text;
using System.Text.Json;
using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Reflection;

/// <summary>The weekly pass that turns observations into at most one belief.</summary>
/// <remarks>
/// "One observation, Sunday night, or nothing" — the architecture's own cadence for it.
/// Everything here is local: observations are read from the corpus and reasoned over by
/// the loopback sidecar, so the most personal pass in the system has no egress
/// dependency at all, and the composition root shows it.
///
/// The model proposes; the service disposes. A proposal must parse, must cite at least
/// one observation by number, and must clear a confidence floor before it becomes a
/// ledger row. Garbage from the model yields a quiet pass, never a crash and never an
/// unsupported belief.
/// </remarks>
public sealed class ReflectionService : IProactiveService
{
    private readonly IObservationCorpus observationCorpus;
    private readonly IConclusionLedger conclusionLedger;
    private readonly IHealthEventStore healthStore;
    private readonly IDomainFactStore domainStore;
    private readonly IObservationEmbeddingStore embeddingStore;
    private readonly IEmbeddingClient embeddingClient;
    private readonly IChatClient chatClient;
    private readonly ReflectionOptions reflectionOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<ReflectionService> logger;

    /// <summary>Creates the service.</summary>
    public ReflectionService(
        IObservationCorpus observationCorpus,
        IConclusionLedger conclusionLedger,
        IHealthEventStore healthStore,
        IDomainFactStore domainStore,
        IObservationEmbeddingStore embeddingStore,
        IEmbeddingClient embeddingClient,
        IChatClient chatClient,
        IOptions<ReflectionOptions> reflectionOptions,
        TimeProvider clock,
        ILogger<ReflectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(observationCorpus);
        ArgumentNullException.ThrowIfNull(conclusionLedger);
        ArgumentNullException.ThrowIfNull(healthStore);
        ArgumentNullException.ThrowIfNull(domainStore);
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(embeddingClient);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(reflectionOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.observationCorpus = observationCorpus;
        this.conclusionLedger = conclusionLedger;
        this.healthStore = healthStore;
        this.domainStore = domainStore;
        this.embeddingStore = embeddingStore;
        this.embeddingClient = embeddingClient;
        this.chatClient = chatClient;
        this.reflectionOptions = reflectionOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "reflection";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Weekly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = this.clock.GetUtcNow();
        var from = context.LastRanAt ?? now.AddDays(-7);
        var observations = await this.CollectAsync(from, now, cancellationToken).ConfigureAwait(false);

        if (observations.Count < this.reflectionOptions.MinimumObservations)
        {
            this.logger.LogInformation(
                "Reflection: {Count} observation(s) since {From}; below the floor of {Floor}, staying quiet",
                observations.Count, from, this.reflectionOptions.MinimumObservations);
            return ProactiveResult.quiet;
        }

        var related = await this.CollectRelatedAsync(observations, cancellationToken).ConfigureAwait(false);
        observations.AddRange(related);

        var conclusion = await this.ProposeAsync(observations, now, cancellationToken).ConfigureAwait(false);

        return conclusion is null
            ? ProactiveResult.quiet
            : new ProactiveResult([conclusion], [], ProactiveStatus.Completed);
    }

    private async Task<List<Observation>> CollectAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var observations = new List<Observation>();
        await foreach (var observation in this.observationCorpus.BetweenAsync(from, to, cancellationToken)
            .ConfigureAwait(false))
        {
            observations.Add(observation);
            if (observations.Count >= this.reflectionOptions.MaximumObservations)
            {
                break;
            }
        }

        return observations;
    }

    private async Task<Conclusion?> ProposeAsync(
        List<Observation> observations,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var believed = await this.CollectBelievedAsync(cancellationToken).ConfigureAwait(false);
        var health = await this.CollectHealthAsync(cancellationToken).ConfigureAwait(false);
        var reply = await this.chatClient
            .CompleteAsync(BuildPrompt(observations, believed, health), cancellationToken)
            .ConfigureAwait(false);

        var conclusion = this.ParseProposal(reply, observations, now);

        // The model saw the believed set and still restated one of its members: discard,
        // or the ledger fills with near-copies and supersession stops meaning anything.
        if (conclusion is not null && IsRestatement(conclusion.Statement, believed))
        {
            this.logger.LogInformation(
                "Reflection: proposal restates an existing belief; discarded: {Statement}",
                conclusion.Statement);
            return null;
        }

        return conclusion;
    }

    /// <summary>Semantically related observations from before the window (RAG over the corpus).</summary>
    /// <remarks>
    /// This is what lets a weekly pass notice a pattern that spans months: the window
    /// supplies the "what happened", the index supplies the "when has this happened
    /// before". Provenance still works because the related items join the same numbered
    /// list the model cites from.
    /// </remarks>
    private async Task<List<Observation>> CollectRelatedAsync(
        List<Observation> window,
        CancellationToken cancellationToken)
    {
        if (this.reflectionOptions.RelatedObservations <= 0 || window.Count == 0)
        {
            return [];
        }

        var themes = new StringBuilder();
        foreach (var observation in window)
        {
            themes.AppendLine(observation.Body);
        }

        var queryVector = (await this.embeddingClient
            .EmbedAsync([themes.ToString()], cancellationToken).ConfigureAwait(false))[0];

        return await this.NearestOutsideAsync(queryVector, window, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<Observation>> NearestOutsideAsync(
        float[] queryVector,
        List<Observation> window,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();
        foreach (var observation in window)
        {
            seen.Add(observation.ObservationId);
        }

        var related = new List<Observation>();
        await foreach (var (observation, _) in this.embeddingStore
            .NearestAsync(
                queryVector,
                this.embeddingClient.ModelId,
                this.reflectionOptions.RelatedObservations + window.Count,
                cancellationToken)
            .ConfigureAwait(false))
        {
            if (seen.Add(observation.ObservationId))
            {
                related.Add(observation);
                if (related.Count >= this.reflectionOptions.RelatedObservations)
                {
                    break;
                }
            }
        }

        return related;
    }

    private async Task<List<string>> CollectBelievedAsync(CancellationToken cancellationToken)
    {
        var believed = new List<string>();
        await foreach (var conclusion in this.conclusionLedger
            .ActiveForSubjectAsync("steve", cancellationToken).ConfigureAwait(false))
        {
            believed.Add(conclusion.Statement);
        }

        return believed;
    }

    private static bool IsRestatement(string statement, List<string> believed)
    {
        foreach (var existing in believed)
        {
            if (string.Equals(existing.Trim(), statement.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The structured health timeline — D-007's cross-domain row set, joined into
    /// the same prompt as the observation window so a pattern can span the two.</summary>
    private async Task<List<string>> CollectHealthAsync(CancellationToken cancellationToken)
    {
        var timeline = new List<string>();
        await foreach (var health in this.healthStore
            .TimelineAsync(this.reflectionOptions.HealthTimelineRows, cancellationToken)
            .ConfigureAwait(false))
        {
            var date = health.EventDate.Year < 1971 ? "undated" : health.EventDate.ToString("yyyy-MM-dd");
            timeline.Add($"{date} [{health.Category}] {health.Description}");
        }

        // The domains after health share one store; their facts join the same prompt.
        await foreach (var fact in this.domainStore
            .TimelineAsync(null, this.reflectionOptions.DomainFactRows, cancellationToken)
            .ConfigureAwait(false))
        {
            timeline.Add($"{fact.AsOf:yyyy-MM-dd} [{fact.Domain}/{fact.Category}] {fact.Description}");
        }

        return timeline;
    }

    private static string BuildPrompt(
        List<Observation> observations,
        List<string> believed,
        List<string> health)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(
            "You are the weekly reflection pass of a personal assistant. Below are numbered");
        prompt.AppendLine(
            "observations of what happened this week. Propose AT MOST ONE durable conclusion");
        prompt.AppendLine(
            "about the person - a pattern worth remembering, not a restatement of one event.");
        prompt.AppendLine(
            "If nothing rises to that bar, answer with exactly: nothing");
        prompt.AppendLine();
        prompt.AppendLine(
            """Otherwise answer with ONLY this JSON: {"statement":"...","confidence":0.0,"supporting":[1,2]}""");
        prompt.AppendLine(
            "where supporting lists the observation numbers that justify the statement.");
        prompt.AppendLine();

        AppendBelieved(prompt, believed);
        AppendHealth(prompt, health);

        for (var index = 0; index < observations.Count; index++)
        {
            prompt.Append(index + 1).Append(". [").Append(observations[index].Source).Append("] ")
                .AppendLine(observations[index].Body);
        }

        return prompt.ToString();
    }

    private static void AppendBelieved(StringBuilder prompt, List<string> believed)
    {
        if (believed.Count == 0)
        {
            return;
        }

        prompt.AppendLine("Already believed - do NOT restate these; propose only something new:");
        foreach (var existing in believed)
        {
            prompt.Append("- ").AppendLine(existing);
        }

        prompt.AppendLine();
    }

    private static void AppendHealth(StringBuilder prompt, List<string> health)
    {
        if (health.Count == 0)
        {
            return;
        }

        // D-007: the join in the prompt. A pattern may connect a health event to the
        // week's observations — that correlation is the whole point of domain rows.
        prompt.AppendLine(
            "Health timeline (correlate with the observations where it is genuinely relevant):");
        foreach (var event_ in health)
        {
            prompt.Append("- ").AppendLine(event_);
        }

        prompt.AppendLine();
    }

    private Conclusion? ParseProposal(string reply, List<Observation> observations, DateTimeOffset now)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            this.logger.LogInformation("Reflection: the model proposed nothing");
            return null;
        }

        try
        {
            return this.BuildConclusion(
                JsonDocument.Parse(reply[start..(end + 1)]).RootElement, observations, now);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            this.logger.LogWarning(exception, "Reflection: unparseable proposal discarded");
            return null;
        }
    }

    private Conclusion? BuildConclusion(
        JsonElement proposal,
        List<Observation> observations,
        DateTimeOffset now)
    {
        var statement = proposal.TryGetProperty("statement", out var statementElement)
            ? statementElement.GetString()
            : null;
        var confidence = proposal.TryGetProperty("confidence", out var confidenceElement)
            ? confidenceElement.GetDouble()
            : 0.0;

        var supporting = MapProvenance(proposal, observations);

        if (string.IsNullOrWhiteSpace(statement)
            || supporting.Count == 0
            || confidence < this.reflectionOptions.MinimumConfidence)
        {
            this.logger.LogInformation(
                "Reflection: proposal rejected (statement: {HasStatement}, provenance: {Provenance}, confidence: {Confidence})",
                !string.IsNullOrWhiteSpace(statement), supporting.Count, confidence);
            return null;
        }

        return new Conclusion(
            Guid.NewGuid(), null, "steve", statement, Math.Clamp(confidence, 0.0, 1.0),
            ConclusionSource.ReflectionPass, now, supporting);
    }

    private static List<Guid> MapProvenance(JsonElement proposal, List<Observation> observations)
    {
        var supporting = new List<Guid>();

        if (!proposal.TryGetProperty("supporting", out var supportingElement))
        {
            return supporting;
        }

        foreach (var number in supportingElement.EnumerateArray())
        {
            var index = number.GetInt32() - 1;
            if (index >= 0 && index < observations.Count)
            {
                supporting.Add(observations[index].ObservationId);
            }
        }

        return supporting;
    }
}
