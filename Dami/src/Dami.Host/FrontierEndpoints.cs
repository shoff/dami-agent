using Dami.Contracts.Approvals;
using Dami.Contracts.Briefs;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Core.Frontier;

namespace Dami.Host;

/// <summary>The two frontier doors: bare questions (ADR-0011) and consent briefs (ADR-0013).</summary>
public static class FrontierEndpoints
{
    /// <summary>Maps the frontier routes.</summary>
    public static void Map(WebApplication app)
    {
        MapBareQuestion(app);
        MapBriefs(app);
    }

    private static void MapBareQuestion(WebApplication app)
    {
        app.MapPost("/frontier", async (
            QuestionRequest request, IFrontierChat frontier, IIdentityProvider identity,
            CancellationToken token) =>
        {
            var traceId = Guid.NewGuid();
            // Acceptance item 9: the same identity, whichever provider answers. The voice
            // line is persona only — no memories, no personal data (ADR-0011 unchanged).
            var answer = await frontier.CompleteAsync(
                new FrontierPrompt(
                    $"{identity.FrontierVoice}\n\n{request.Question}",
                    "api frontier question", PrivacyClass.Egressable,
                    traceId, ExecutionOrigin.UserTurn),
                token).ConfigureAwait(false);
            return Results.Ok(new { traceId, answer });
        });

    }

    private static void MapBriefs(WebApplication app)
    {
        app.MapPost("/briefs", async (
            QuestionRequest request, IContextBuilder contextBuilder, IPromptRedactor redactor,
            IApprovalService approvals, IEgressBriefStore briefStore, TimeProvider clock,
            CancellationToken token) =>
        {
            var traceId = Guid.NewGuid();
            var context = await contextBuilder.BuildAsync(request.Question, token).ConfigureAwait(false);
            var lines = context.Beliefs.Concat(context.Memories)
                .Select(item => item.Content).ToList();
            var draft = await redactor.DraftAsync(request.Question, lines, token).ConfigureAwait(false);

            var approval = new ApprovalRequest(
                Guid.NewGuid(), traceId, "frontier-brief",
                "send a redacted, memory-informed brief to the frontier",
                "egress", "codex subscription", clock.GetUtcNow());
            await approvals.RequestAsync(approval, token).ConfigureAwait(false);
            var brief = new EgressBrief(
                Guid.NewGuid(), approval.ApprovalId, traceId, request.Question, draft,
                BriefExecutor.HashOf(draft), clock.GetUtcNow());
            await briefStore.CreateAsync(brief, token).ConfigureAwait(false);

            return Results.Ok(new
            {
                approvalId = approval.ApprovalId,
                brief = draft,
                sha256 = brief.BriefSha256,
                contextItems = lines.Count,
            });
        });
    }
}
