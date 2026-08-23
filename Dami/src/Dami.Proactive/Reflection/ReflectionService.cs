using System.Text;
using System.Text.Json;
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
    private readonly IChatClient chatClient;
    private readonly ReflectionOptions reflectionOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<ReflectionService> logger;

    /// <summary>Creates the service.</summary>
    public ReflectionService(
        IObservationCorpus observationCorpus,
        IChatClient chatClient,
        IOptions<ReflectionOptions> reflectionOptions,
        TimeProvider clock,
        ILogger<ReflectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(observationCorpus);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(reflectionOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.observationCorpus = observationCorpus;
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

        var reply = await this.chatClient
            .CompleteAsync(BuildPrompt(observations), cancellationToken).ConfigureAwait(false);

        var conclusion = this.ParseProposal(reply, observations, now);
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

    private static string BuildPrompt(List<Observation> observations)
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

        for (var index = 0; index < observations.Count; index++)
        {
            prompt.Append(index + 1).Append(". [").Append(observations[index].Source).Append("] ")
                .AppendLine(observations[index].Body);
        }

        return prompt.ToString();
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
