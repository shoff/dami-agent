using System.Text.Json;
using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Core.Context;

/// <summary>Plans retrieval with the local model (ADR-0019).</summary>
/// <remarks>
/// Two passes, because the order matters. The first routes the question to the domains
/// that bear on it and drafts searches; if it named a domain, the second redrafts those
/// searches with that domain's facts in hand. Measured on "given my heart condition, what
/// should I ask the surgeon": ungrounded the model returns "heart condition treatment
/// options", which matches nothing the corpus wrote; grounded it returns "severe aortic
/// stenosis" and "mechanical AVR surgery", which is the vocabulary the notes use. A
/// question naming no domain costs one pass, as before.
///
/// Fails open, unlike <c>LocalDisclosureGate</c>. A gate that cannot parse its answer must
/// withhold, because the cost of guessing is a privacy breach; a planner that cannot parse
/// its answer falls back to searching for the request verbatim, which is what retrieval did
/// before this existed. Degrading to "merely as good as yesterday" needs no ceremony.
/// </remarks>
public sealed class LocalQueryPlanner : IQueryPlanner
{
    private readonly IChatClient chatClient;
    private readonly IReadOnlyList<IStructuredFactSource> factSources;
    private readonly QueryPlanOptions options;
    private readonly ILogger<LocalQueryPlanner> logger;

    /// <summary>Creates a planner.</summary>
    public LocalQueryPlanner(
        IChatClient chatClient,
        IEnumerable<IStructuredFactSource> factSources,
        IOptions<QueryPlanOptions> options,
        ILogger<LocalQueryPlanner> logger)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(factSources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.chatClient = chatClient;
        this.factSources = factSources.ToList();
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<QueryPlan> PlanAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fallback = new QueryPlan([request], [], []);
        if (!this.options.Enabled)
        {
            return fallback;
        }

        var draft = await this.DraftAsync(request, cancellationToken).ConfigureAwait(false);
        if (draft is null)
        {
            this.logger.LogWarning("No usable query plan; searching the request verbatim.");
            return fallback;
        }

        var facts = await this.GatherAsync(request, draft.Domains, cancellationToken).ConfigureAwait(false);
        var searches = facts.Count == 0
            ? draft.Searches
            : await this.GroundAsync(request, facts, draft.Searches, cancellationToken).ConfigureAwait(false);

        this.logger.LogInformation(
            "Query plan: {Searches} search(es), {Facts} fact(s) from [{Domains}].",
            searches.Count, facts.Count, string.Join(", ", draft.Domains));
        return new QueryPlan(searches, draft.Domains, facts);
    }

    /// <summary>Pass one: which domains bear on this, and a first draft of searches.</summary>
    private async Task<QueryPlan?> DraftAsync(string request, CancellationToken cancellationToken)
    {
        var reply = await this.AskAsync(this.DraftPrompt(request), cancellationToken).ConfigureAwait(false);
        return reply is null ? null : Parse(reply, request, this.options);
    }

    /// <summary>Pass two: the same searches, rewritten in the words the notes actually use.</summary>
    private async Task<IReadOnlyList<string>> GroundAsync(
        string request,
        IReadOnlyList<StructuredFact> facts,
        IReadOnlyList<string> draft,
        CancellationToken cancellationToken)
    {
        var reply = await this.AskAsync(this.GroundPrompt(request, facts), cancellationToken)
            .ConfigureAwait(false);
        var grounded = reply is null ? null : Parse(reply, request, this.options);

        // A failed second pass keeps the first pass's searches. Grounding is an
        // improvement on the draft, never a precondition for having one.
        return grounded?.Searches ?? draft;
    }

    private async Task<List<StructuredFact>> GatherAsync(
        string request,
        IReadOnlyList<string> domains,
        CancellationToken cancellationToken)
    {
        var facts = new List<StructuredFact>();
        foreach (var source in this.factSources)
        {
            if (!domains.Contains(source.Domain))
            {
                continue;
            }

            await foreach (var fact in source
                .RelevantAsync(request, this.options.FactsPerDomain, cancellationToken)
                .ConfigureAwait(false))
            {
                facts.Add(fact);
            }
        }

        return facts;
    }

    private async Task<string?> AskAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            return await this.chatClient.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            this.logger.LogWarning(error, "The planner model did not answer.");
            return null;
        }
    }

    private static QueryPlan? Parse(string reply, string request, QueryPlanOptions options)
    {
        var root = Json(reply);
        if (root is null)
        {
            return null;
        }

        var searches = Strings(root.Value, "searches")
            .Where(text => text.Length > 2)
            .Take(options.MaxSearches)
            .ToList();

        // The request itself always stays in the plan: an expansion that drifts off the
        // question still cannot lose the one query known to be on it.
        if (!searches.Any(search => string.Equals(search, request, StringComparison.OrdinalIgnoreCase)))
        {
            searches.Insert(0, request);
        }

        var domains = Strings(root.Value, "domains")
            .Select(name => name.ToLowerInvariant())
            .Where(options.KnownDomains.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new QueryPlan(searches, domains, []);
    }

    private static JsonElement? Json(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(reply[start..(end + 1)]).RootElement;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Strings(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString()!.Trim())
            .Where(text => text.Length > 0);
    }

    private static string Describe(StructuredFact fact)
        => fact.AsOf is null
            ? $"{fact.Kind}: {fact.Text}"
            : $"{fact.AsOf:yyyy-MM-dd} {fact.Kind}: {fact.Text}";

    private string DraftPrompt(string request)
        => $$"""
            You prepare searches for a personal assistant's memory. Do not answer the question.

            Question: {{request}}

            Write up to {{this.options.MaxSearches}} short search queries that would find the
            stored notes needed to answer it, covering its distinct parts separately. Keep one
            query close to the original wording.

            Then list which of these domains hold facts bearing on the question, or none:
            {{string.Join(", ", this.options.KnownDomains)}}

            Reply with JSON only:
            {"searches": ["...", "..."], "domains": ["..."]}
            """;

    private string GroundPrompt(string request, IReadOnlyList<StructuredFact> facts)
        => $$"""
            You prepare searches for a personal assistant's memory. Do not answer the question.

            Question: {{request}}

            Known facts about this person:
            {{string.Join("\n", facts.Select(Describe))}}

            Write up to {{this.options.MaxSearches}} short search queries that would find the
            stored notes needed to answer the question. Replace the vague words the question
            used with the specific terms from the known facts above, so the queries match how
            the notes are written. Keep one query close to the original wording.

            Reply with JSON only:
            {"searches": ["...", "..."]}
            """;
}
