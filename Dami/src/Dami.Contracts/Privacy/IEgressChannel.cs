namespace Dami.Contracts.Privacy;

/// <summary>Where a piece of outbound content came from, which decides whether it may leave.</summary>
/// <remarks>
/// D-012 draws its line at the profile, not at the network: a feed URL and a board state
/// are both fine to send, while an answer shaped by Steve's memory is not. The provenance
/// has to be stated by the caller because it cannot be recovered from the text — "he is
/// travelling on Tuesday" looks like any other sentence once it is a string.
/// </remarks>
public enum ContentProvenance
{
    /// <summary>
    /// Produced by the system about itself — board state, service status, a surfacing
    /// headline, an error. Contains nothing drawn from the profile or the corpus.
    /// </summary>
    Operational,

    /// <summary>
    /// Shaped by Steve's memory, observations, or profile. Refused by every channel
    /// under ADR-0024; lifting that is a D-012 amendment, not a setting.
    /// </summary>
    ProfileDerived,
}

/// <summary>Something a person sent to Dami over a channel.</summary>
public sealed record InboundMessage(
    string AuthorId,
    string ConversationId,
    string Text,
    DateTimeOffset ReceivedAt);

/// <summary>Something Dami is trying to send out over a channel.</summary>
public sealed record OutboundContent(
    string ConversationId,
    string Text,
    ContentProvenance Provenance,
    Guid TraceId);

/// <summary>
/// A persistent, single-destination link that carries content in both directions (ADR-0024).
/// </summary>
/// <remarks>
/// D-012's second mechanism, and deliberately not a widening of the first.
/// <see cref="IEgressClient"/> fetches: it has a destination and a purpose and no body,
/// because the narrowness of that shape is what stops the profile leaving through it. A
/// messaging gateway cannot be expressed that way — replying carries a body, and the
/// connection outlives any one request — so it gets its own mechanism rather than
/// hollowing out the existing one for every caller.
///
/// The cost of a second mechanism is a second thing to audit. That is paid for in the
/// composition root, where holding a channel must be as visible as holding a client, and
/// in the architecture tests, which forbid a local-only service from receiving either.
/// </remarks>
public interface IEgressChannel
{
    /// <summary>Which channel this is, for events, logs and the authority lease.</summary>
    string ChannelName { get; }

    /// <summary>Sends content, if its provenance permits.</summary>
    /// <exception cref="EgressRefusedException">
    /// The content is profile-derived, or the conversation is not the one this channel
    /// is bound to.
    /// </exception>
    Task SendAsync(OutboundContent content, CancellationToken cancellationToken);

    /// <summary>Yields messages as they arrive, until cancelled.</summary>
    IAsyncEnumerable<InboundMessage> ListenAsync(CancellationToken cancellationToken);
}
