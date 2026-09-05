using Dami.Contracts.Privacy;

namespace Dami.Host.Discord;

/// <summary>What the gateway says when it cannot pass on a frontier answer.</summary>
/// <remarks>
/// Pure, and every message here is Operational: each is a fact about the system rather
/// than about Steve, which is what lets it leave through a channel that would refuse
/// profile-derived text. Nothing in this class writes an answer to the question — that
/// is the frontier's alone (ADR-0028).
/// </remarks>
public static class DiscordAnswer
{
    private const int MAX_REASON_LENGTH = 160;

    /// <summary>
    /// What to say instead when the answer cannot leave — itself operational, so it can.
    /// </summary>
    /// <remarks>
    /// Silence would be the wrong failure. Steve asked a question and is owed the reason
    /// he is not getting an answer, and "the boundary refused this" is a fact about the
    /// system rather than a fact about him.
    /// </remarks>
    public static OutboundContent Refusal(string conversationId, Guid traceId) =>
        new(
            conversationId,
            "That answer draws on local memory and this channel is not addressed to you "
            + $"(ADR-0025). It is on the host — trace {traceId}.",
            ContentProvenance.Operational,
            traceId);

    /// <summary>
    /// What to say when the frontier produced no answer. There is no other author.
    /// </summary>
    /// <remarks>
    /// ADR-0026 let the local model answer here "so a hiccup degrades the answer, not the
    /// gateway". It degraded the answer to one Steve had said he never wanted, twice in a
    /// night. The honest degradation is no answer and the reason.
    /// </remarks>
    public static OutboundContent FrontierUnavailable(
        string conversationId, Guid traceId, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        var line = reason.ReplaceLineEndings(" ").Trim();
        if (line.Length > MAX_REASON_LENGTH)
        {
            line = line[..MAX_REASON_LENGTH] + "…";
        }

        return new OutboundContent(
            conversationId,
            $"The frontier did not answer that: {line}. Nothing was answered locally — "
            + $"this channel answers only from the frontier (ADR-0028). Trace {traceId}.",
            ContentProvenance.Operational,
            traceId);
    }
}
