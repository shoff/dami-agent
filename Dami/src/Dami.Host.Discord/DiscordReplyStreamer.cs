using System.Text;
using Dami.Contracts.Privacy;
using Dami.Core.Frontier;

namespace Dami.Host.Discord;

/// <summary>Turns model fragments into one progressively edited Discord reply.</summary>
public sealed class DiscordReplyStreamer
{
    private const int EDIT_CHARACTER_INTERVAL = 80;

    private readonly IProgressiveEgressChannel channel;

    /// <summary>Creates the streamer.</summary>
    public DiscordReplyStreamer(IProgressiveEgressChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        this.channel = channel;
    }

    /// <summary>Publishes the first fragment immediately and reconciles the final text.</summary>
    public async Task<string> StreamAsync(
        string conversationId,
        AugmentedTurnStream stream,
        CancellationToken cancellationToken)
    {
        var answer = new StringBuilder();
        string? messageId = null;
        var publishedLength = 0;
        await foreach (var fragment in stream.Tokens.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (fragment.Length == 0)
            {
                continue;
            }

            answer.Append(fragment);
            if (messageId is null)
            {
                messageId = await this.BeginAsync(
                    conversationId, stream.TraceId, answer.ToString(), cancellationToken).ConfigureAwait(false);
                publishedLength = answer.Length;
            }
            else if (answer.Length - publishedLength >= EDIT_CHARACTER_INTERVAL)
            {
                await this.UpdateAsync(
                    conversationId, stream.TraceId, messageId, answer.ToString(), cancellationToken).ConfigureAwait(false);
                publishedLength = answer.Length;
            }
        }

        var text = answer.ToString();
        await this.FinalizeAsync(
            conversationId, stream.TraceId, messageId, publishedLength, text, cancellationToken)
            .ConfigureAwait(false);
        return text;
    }

    private Task<string> BeginAsync(
        string conversationId, Guid traceId, string text, CancellationToken cancellationToken) =>
        this.channel.BeginAsync(
            new OutboundContent(
                conversationId, text, ContentProvenance.ProfileDerived, traceId),
            cancellationToken);

    private async Task FinalizeAsync(
        string conversationId,
        Guid traceId,
        string? messageId,
        int publishedLength,
        string text,
        CancellationToken cancellationToken)
    {
        var content = new OutboundContent(
            conversationId, text, ContentProvenance.ProfileDerived, traceId);
        if (messageId is null)
        {
            await this.channel.BeginAsync(content, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (publishedLength != text.Length)
        {
            await this.channel.UpdateAsync(messageId, content, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task UpdateAsync(
        string conversationId,
        Guid traceId,
        string messageId,
        string text,
        CancellationToken cancellationToken) =>
        this.channel.UpdateAsync(
            messageId,
            new OutboundContent(
                conversationId, text, ContentProvenance.ProfileDerived, traceId),
            cancellationToken);
}
