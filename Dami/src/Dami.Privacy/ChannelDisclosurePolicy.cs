using Dami.Contracts.Privacy;

namespace Dami.Privacy;

/// <summary>Whether one piece of content may cross a channel, and whether one message counts.</summary>
/// <remarks>
/// Pure and static so the boundary can be argued with in tests rather than inferred from
/// a running socket. This is the whole of ADR-0024's enforcement: everything above it is
/// transport, and transport has a habit of growing exceptions.
///
/// Both directions are guarded, for different reasons. Outbound protects Steve from the
/// system — profile-derived content does not leave. Inbound protects the system from
/// everyone else — a bot sitting in a server will be spoken to by other members, and
/// anything they say would otherwise arrive as an instruction to the runtime.
/// </remarks>
public static class ChannelDisclosurePolicy
{
    /// <summary>Refuses content whose provenance forbids it reaching this recipient.</summary>
    /// <remarks>
    /// ADR-0025: the test is who receives it, not what it contains. D-012 protects Steve's
    /// profile from disclosure to others; sending Steve his own memory back to Steve is not
    /// a disclosure, and a rule that refused it produced a gateway which answered "hi there"
    /// by citing its own decision record.
    ///
    /// The permission is to the person and never to the transport. A future channel whose
    /// reader is anyone else — a shared guild, a family calendar, a public bot — passes
    /// false here and gets ADR-0024's behaviour back unchanged.
    /// </remarks>
    /// <param name="content">What is being sent.</param>
    /// <param name="channelName">The channel, for the refusal message.</param>
    /// <param name="recipientIsDataSubject">
    /// Whether the only reader is the person the profile is about.
    /// </param>
    /// <exception cref="EgressRefusedException">
    /// The content is profile-derived and the recipient is someone else.
    /// </exception>
    public static void EnsureMayLeave(
        OutboundContent content, string channelName, bool recipientIsDataSubject)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(channelName);

        if (content.Provenance is ContentProvenance.ProfileDerived && !recipientIsDataSubject)
        {
            throw new EgressRefusedException(
                $"{channelName} refused profile-derived content addressed to someone other than "
                + $"its subject (ADR-0025). Trace {content.TraceId}.");
        }
    }

    /// <summary>
    /// Whether a message should be acted on: it is from the bound person, in the bound
    /// conversation, and is not the bot's own.
    /// </summary>
    /// <remarks>
    /// Returning false rather than throwing, because most traffic on a shared channel is
    /// legitimately not for Dami. An unrecognised author is the ordinary case, not a fault.
    /// </remarks>
    public static bool ShouldAnswer(InboundMessage message, string ownerId, string selfId)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(selfId);

        if (message.Text.Trim().Length == 0)
        {
            return false;
        }

        return !string.Equals(message.AuthorId, selfId, StringComparison.Ordinal)
            && string.Equals(message.AuthorId, ownerId, StringComparison.Ordinal);
    }
}
