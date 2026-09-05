using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Handles explicit Discord image-generation commands.</summary>
public sealed class DiscordImageResponder
{
    private readonly IImageGenerator generator;
    private readonly IPortraitGenerator portraits;
    private readonly IEgressChannel channel;
    private readonly ILogger<DiscordImageResponder> logger;

    /// <summary>Creates the responder.</summary>
    public DiscordImageResponder(
        IImageGenerator generator,
        IPortraitGenerator portraits,
        IEgressChannel channel,
        ILogger<DiscordImageResponder> logger)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(portraits);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(logger);
        this.generator = generator;
        this.portraits = portraits;
        this.channel = channel;
        this.logger = logger;
    }

    /// <summary>Generates an image when the message uses the explicit command.</summary>
    public async Task<bool> TryAnswerAsync(
        InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // A message carrying a picture is about that picture, never a request for one.
        if (message.Attachments.Count > 0)
        {
            return false;
        }

        var request = DiscordImageIntent.Classify(message.Text);
        if (request is null)
        {
            return false;
        }

        var traceId = Guid.NewGuid();
        try
        {
            var image = await this.GenerateAsync(request, traceId, cancellationToken).ConfigureAwait(false);
            await this.SendAsync(message.ConversationId, traceId, image, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Discord image generation failed");
            await this.SendFailureAsync(message.ConversationId, traceId, cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>A picture of Dami keeps her identity; anything else is drawn as asked.</summary>
    private Task<GeneratedImage> GenerateAsync(
        DiscordImageRequest request, Guid traceId, CancellationToken cancellationToken) =>
        request.OfDami
            ? this.portraits.GenerateAsync(request.Text, cancellationToken)
            : this.generator.GenerateAsync(
                new ImageRequest(
                    request.Text, "Discord image request", PrivacyClass.Egressable,
                    traceId, ExecutionOrigin.UserTurn),
                cancellationToken);

    private Task SendAsync(
        string conversationId,
        Guid traceId,
        GeneratedImage image,
        CancellationToken cancellationToken) =>
        this.channel.SendAsync(
            new OutboundContent(
                conversationId, "Here’s the image.", ContentProvenance.Operational, traceId)
            {
                Attachments = [new OutboundAttachment(image.FileName, image.Bytes, image.ContentType)],
            },
            cancellationToken);

    private Task SendFailureAsync(
        string conversationId, Guid traceId, CancellationToken cancellationToken) =>
        this.channel.SendAsync(
            new OutboundContent(
                conversationId,
                "I couldn’t create that image. Check the image-provider configuration and logs.",
                ContentProvenance.Operational,
                traceId),
            cancellationToken);
}
