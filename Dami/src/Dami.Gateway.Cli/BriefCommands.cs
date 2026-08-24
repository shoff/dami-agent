using Dami.Contracts.Approvals;
using Dami.Contracts.Briefs;
using Dami.Contracts.Context;
using Dami.Contracts.Privacy;
using Dami.Core.Frontier;

namespace Dami.Gateway.Cli;

/// <summary>`dami brief` — draft a redacted, memory-informed question for the frontier (C4).</summary>
/// <remarks>
/// The flow: context is assembled locally, the local model drafts a redacted brief,
/// the exact bytes are stored hash-pinned behind a pending approval, and the full text
/// is printed for review. Nothing egresses here — `dami approve` does that, through
/// <see cref="BriefExecutor"/>, and only if the bytes still match.
/// </remarks>
public sealed class BriefCommands
{
    private readonly IContextBuilder contextBuilder;
    private readonly IPromptRedactor promptRedactor;
    private readonly IApprovalService approvalService;
    private readonly IEgressBriefStore briefStore;
    private readonly TimeProvider clock;

    /// <summary>Creates the commands.</summary>
    public BriefCommands(
        IContextBuilder contextBuilder,
        IPromptRedactor promptRedactor,
        IApprovalService approvalService,
        IEgressBriefStore briefStore,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(contextBuilder);
        ArgumentNullException.ThrowIfNull(promptRedactor);
        ArgumentNullException.ThrowIfNull(approvalService);
        ArgumentNullException.ThrowIfNull(briefStore);
        ArgumentNullException.ThrowIfNull(clock);

        this.contextBuilder = contextBuilder;
        this.promptRedactor = promptRedactor;
        this.approvalService = approvalService;
        this.briefStore = briefStore;
        this.clock = clock;
    }

    /// <summary>Drafts the brief and files the approval. Prints the exact bytes for review.</summary>
    public async Task<int> DraftAsync(string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        var traceId = Guid.NewGuid();
        Console.WriteLine("assembling context and drafting a redacted brief (local model)...");

        var context = await this.contextBuilder.BuildAsync(question, cancellationToken).ConfigureAwait(false);
        var lines = context.Beliefs.Concat(context.Memories).Select(item => item.Content).ToList();
        var draft = await this.promptRedactor.DraftAsync(question, lines, cancellationToken)
            .ConfigureAwait(false);

        var approvalId = await this.FileApprovalAsync(traceId, cancellationToken).ConfigureAwait(false);
        var brief = new EgressBrief(
            Guid.NewGuid(), approvalId, traceId, question, draft,
            BriefExecutor.HashOf(draft), this.clock.GetUtcNow());
        await this.briefStore.CreateAsync(brief, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("---- exact bytes that would egress ----");
        Console.WriteLine(draft);
        Console.WriteLine("---------------------------------------");
        Console.WriteLine($"context drawn from {lines.Count} item(s); sha256 {brief.BriefSha256[..12]}…");
        Console.WriteLine($"review the text above, then: dami approve {approvalId.ToString("N")[..8]}");
        Console.WriteLine($"or: dami deny {approvalId.ToString("N")[..8]} \"reason\"");
        return 0;
    }

    private Task<Guid> FileApprovalAsync(Guid traceId, CancellationToken cancellationToken)
    {
        var request = new ApprovalRequest(
            Guid.NewGuid(), traceId, "frontier-brief",
            "send a redacted, memory-informed brief to the frontier",
            "egress", "codex subscription", this.clock.GetUtcNow());
        return this.RequestAsync(request, cancellationToken);
    }

    private async Task<Guid> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        await this.approvalService.RequestAsync(request, cancellationToken).ConfigureAwait(false);
        return request.ApprovalId;
    }
}
