using Dami.Contracts.Context;
using Dami.Contracts.Privacy;
using Dami.Core.Turns;

namespace Dami.Host.Discord;

/// <summary>Classifies a turn's answer for the egress channel.</summary>
/// <remarks>
/// Pure, because this is where ADR-0024 is either enforced or quietly not. The
/// classification reuses the routing decision the system already made rather than
/// inventing a second opinion: <c>ModelRoute.Privacy</c> is what D-012 branches
/// on everywhere else, and a channel that disagreed with the router would be a boundary
/// with two answers.
///
/// The retrieved-context check is belt and braces. If memories or beliefs entered the
/// prompt then the answer is shaped by them whatever the route says, and the conservative
/// reading is the one that keeps the profile at home.
/// </remarks>
public static class DiscordAnswer
{
    /// <summary>How the answer may be treated on the way out.</summary>
    public static ContentProvenance ProvenanceOf(TurnResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var routedLocalOnly = result.Route.Privacy == PrivacyClass.LocalOnly;
        var usedProfile = result.Context.Memories.Count > 0 || result.Context.Beliefs.Count > 0;

        return routedLocalOnly || usedProfile
            ? ContentProvenance.ProfileDerived
            : ContentProvenance.Operational;
    }

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
}
