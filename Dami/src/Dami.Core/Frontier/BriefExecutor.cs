using System.Security.Cryptography;
using System.Text;
using Dami.Contracts.Approvals;
using Dami.Contracts.Briefs;
using Dami.Contracts.Events;
using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Frontier;

/// <summary>Sends an approved brief to the frontier — and only an approved one, byte-exactly.</summary>
/// <remarks>
/// The consent contract (C4): the approval covers a SHA-256 of the brief text. This
/// executor recomputes the hash at send time and refuses on mismatch, so nothing can
/// swap the reviewed bytes between approval and egress. Refusals are loud.
/// </remarks>
public sealed class BriefExecutor
{
    private readonly IApprovalService approvalService;
    private readonly IEgressBriefStore briefStore;
    private readonly IFrontierChat frontierChat;
    private readonly TimeProvider clock;
    private readonly ILogger<BriefExecutor> logger;

    /// <summary>Creates the executor.</summary>
    public BriefExecutor(
        IApprovalService approvalService,
        IEgressBriefStore briefStore,
        IFrontierChat frontierChat,
        TimeProvider clock,
        ILogger<BriefExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(approvalService);
        ArgumentNullException.ThrowIfNull(briefStore);
        ArgumentNullException.ThrowIfNull(frontierChat);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.approvalService = approvalService;
        this.briefStore = briefStore;
        this.frontierChat = frontierChat;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Computes the hash the approval pins.</summary>
    public static string HashOf(string brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(brief)));
    }

    /// <summary>Sends the brief behind an Approved approval. Returns the frontier's answer.</summary>
    public async Task<string> ExecuteAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        var approval = await this.approvalService.FindAsync(approvalId, cancellationToken)
            .ConfigureAwait(false);
        if (approval is null || approval.Status != ApprovalStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Approval {approvalId} is not in the Approved state; the brief does not egress.");
        }

        var brief = await this.briefStore.FindByApprovalAsync(approvalId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No brief is attached to approval {approvalId}.");

        if (!string.Equals(HashOf(brief.Brief), brief.BriefSha256, StringComparison.Ordinal))
        {
            this.logger.LogError("Brief {Brief} failed its hash check; refusing to send", brief.BriefId);
            throw new InvalidOperationException(
                $"Brief {brief.BriefId} does not match the hash that was approved; refusing to send.");
        }

        var answer = await this.frontierChat.CompleteAsync(
            new FrontierPrompt(
                brief.Brief, $"approved brief {brief.BriefId.ToString("N")[..8]}",
                PrivacyClass.Egressable, brief.TraceId, ExecutionOrigin.UserTurn),
            cancellationToken).ConfigureAwait(false);

        await this.briefStore.MarkSentAsync(
            brief.BriefId, answer, this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return answer;
    }
}
