using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Describes the images on a message, locally.</summary>
/// <remarks>
/// The bytes go to loopback and nowhere else: <see cref="IVisionClient"/> is LocalOnly by
/// contract, and the picture itself never becomes part of anything that egresses. Only the
/// caption travels on, and only after the disclosure gate has seen it like any other
/// context (ADR-0026).
/// </remarks>
public sealed class DiscordVision
{
    /// <summary>Past this, a download is more likely a mistake than a photo worth reading.</summary>
    private const int MAX_BYTES = 12 * 1024 * 1024;

    /// <summary>Images described per message; the rest are named but not read.</summary>
    private const int MAX_IMAGES = 4;

    private const string PROMPT =
        "Describe this image plainly and concretely for someone who cannot see it. "
        + "Include any text that appears in it. Two or three sentences.";

    private readonly IVisionClient vision;
    private readonly IDiscordRest rest;
    private readonly ILogger<DiscordVision> logger;

    /// <summary>Creates the describer.</summary>
    public DiscordVision(IVisionClient vision, IDiscordRest rest, ILogger<DiscordVision> logger)
    {
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(logger);

        this.vision = vision;
        this.rest = rest;
        this.logger = logger;
    }

    /// <summary>Captions for every readable image on the message, in order.</summary>
    public async Task<IReadOnlyList<string>> DescribeAsync(
        InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var captions = new List<string>();
        foreach (var attachment in message.Attachments)
        {
            if (!attachment.IsImage || captions.Count >= MAX_IMAGES)
            {
                continue;
            }

            if (attachment.SizeBytes > MAX_BYTES)
            {
                captions.Add($"{attachment.FileName} is too large to read ({attachment.SizeBytes} bytes).");
                continue;
            }

            var caption = await this.DescribeOneAsync(attachment, cancellationToken)
                .ConfigureAwait(false);
            if (caption is not null)
            {
                captions.Add(caption);
            }
        }

        return captions;
    }

    private async Task<string?> DescribeOneAsync(
        InboundAttachment attachment, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await this.rest.DownloadAsync(attachment.Url, cancellationToken)
                .ConfigureAwait(false);
            var caption = await this.vision.DescribeAsync(bytes, PROMPT, cancellationToken)
                .ConfigureAwait(false);
            return $"{attachment.FileName}: {caption.Trim()}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A picture that cannot be read is worth saying so about; the rest of the
            // message is still answerable.
            this.logger.LogWarning(
                exception, "Could not describe Discord attachment {File}", attachment.FileName);
            return $"{attachment.FileName} could not be read.";
        }
    }
}
