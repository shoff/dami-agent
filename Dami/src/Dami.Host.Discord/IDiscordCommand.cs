using Dami.Contracts.Privacy;

namespace Dami.Host.Discord;

/// <summary>An exact text command answered from runtime state, before anything else sees the message.</summary>
public interface IDiscordCommand
{
    /// <summary>Answers when the message is this command; false to let the next one look.</summary>
    Task<bool> TryAnswerAsync(InboundMessage message, CancellationToken cancellationToken);
}
