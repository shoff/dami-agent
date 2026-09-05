using System.Text.Json;
using Dami.Contracts.Briefs;
using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Core.Frontier;

/// <summary>Lets the frontier look something up in Steve's memory mid-turn (ADR-0030).</summary>
public interface IFrontierRecall
{
    /// <summary>The tool as offered to the frontier.</summary>
    FrontierTool Tool { get; }

    /// <summary>Retrieves, gates, records, and answers — or says why there is nothing to say.</summary>
    Task<FrontierToolResult> RecallAsync(Guid traceId, string query, CancellationToken cancellationToken);
}

/// <summary>
/// Recall through the same boundary as retrieved context: local retrieval, the disclosure
/// gate per item, the decision in the ledger, and the bytes that left in an egress brief.
/// </summary>
/// <remarks>
/// The up-front retrieval on an augmented turn is keyed to the question as asked. This is
/// for the second look — "what did I lift Monday" after the first pass fetched Tuesday —
/// and it must not become a side door: everything that leaves here is judged and
/// recorded exactly as the first pass would have judged and recorded it (D-012).
/// </remarks>
public sealed class FrontierRecallTool : IFrontierRecall
{
    /// <summary>The tool's name on the wire.</summary>
    public const string NAME = "recall";

    private readonly IContextBuilder contextBuilder;
    private readonly IContextDisclosureGate gate;
    private readonly IDisclosureLedger disclosureLedger;
    private readonly IEgressBriefStore briefStore;
    private readonly AugmentedTurnOptions turnOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<FrontierRecallTool> logger;

    /// <summary>Creates the tool.</summary>
    public FrontierRecallTool(
        IContextBuilder contextBuilder,
        IContextDisclosureGate gate,
        IDisclosureLedger disclosureLedger,
        IEgressBriefStore briefStore,
        IOptions<AugmentedTurnOptions> turnOptions,
        TimeProvider clock,
        ILogger<FrontierRecallTool> logger)
    {
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(disclosureLedger);
        ArgumentNullException.ThrowIfNull(briefStore);
        ArgumentNullException.ThrowIfNull(turnOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.contextBuilder = contextBuilder;
        this.gate = gate;
        this.disclosureLedger = disclosureLedger;
        this.briefStore = briefStore;
        this.turnOptions = turnOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public FrontierTool Tool { get; } = new(
        NAME,
        "Search Steve's own memory: things he has told you, his notes, beliefs about him, his "
        + "health and training log, his gear, his plans and his people. Use it when a question "
        + "is about his past or his life and the context you were given does not already "
        + "answer it. Returns the matching notes; some may be withheld for privacy, and that is "
        + "final — do not ask for them another way.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "What to look for, as a short search phrase." },
            },
            required = new[] { "query" },
            additionalProperties = false,
        }));

    /// <inheritdoc />
    public async Task<FrontierToolResult> RecallAsync(
        Guid traceId, string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var context = await this.contextBuilder.BuildAsync(query, cancellationToken).ConfigureAwait(false);
        var lines = context.Beliefs.Concat(context.Memories).Select(item => item.Content).ToList();
        if (lines.Count == 0)
        {
            return FrontierToolResult.Ok("Nothing in memory matches that.");
        }

        var decided = await this.DecideAsync(traceId, query, lines, cancellationToken).ConfigureAwait(false);
        var sendable = decided.Where(item => item.Disclosure != Disclosure.Withhold).ToList();
        this.logger.LogInformation(
            "Recall '{Query}': {Found} found, {Sent} sent, {Withheld} withheld",
            query, lines.Count, sendable.Count, decided.Count - sendable.Count);
        if (sendable.Count == 0)
        {
            return FrontierToolResult.Ok(
                "Memory has notes on that, but they stay on this host (withheld by the disclosure gate).");
        }

        var text = string.Join('\n', sendable.Select(item => "- " + item.Sendable));
        await this.RecordAsync(traceId, query, text, cancellationToken).ConfigureAwait(false);
        return FrontierToolResult.Ok(text);
    }

    private async Task<IReadOnlyList<DisclosedItem>> DecideAsync(
        Guid traceId, string query, List<string> lines, CancellationToken cancellationToken)
    {
        if (!this.turnOptions.Gate)
        {
            return [.. lines.Select(line => new DisclosedItem(line, Disclosure.Pass, line, "gate disabled"))];
        }

        var decided = await this.gate.ClassifyAsync(query, lines, cancellationToken).ConfigureAwait(false);
        await this.disclosureLedger.RecordAsync(
            traceId, NAME + ": " + query, decided, this.clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        return decided;
    }

    /// <summary>The bytes that left, hash-pinned, beside the turn's own brief.</summary>
    private Task RecordAsync(Guid traceId, string query, string sent, CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        return this.briefStore.CreateAsync(
            new EgressBrief(
                Guid.NewGuid(), null, traceId, NAME + ": " + query, sent,
                BriefExecutor.HashOf(sent), now, now),
            cancellationToken);
    }
}
