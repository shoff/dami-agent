using Dami.Contracts.Models;

namespace Dami.Host;

/// <summary>Turns a GUI attachment into local, gateable text without egressing its bytes.</summary>
public sealed class TurnImageContext
{
    private const int MAX_IMAGE_BYTES = 12 * 1024 * 1024;
    private readonly IVisionClient vision;

    /// <summary>Creates the local image bridge.</summary>
    public TurnImageContext(IVisionClient vision)
    {
        ArgumentNullException.ThrowIfNull(vision);
        this.vision = vision;
    }

    /// <summary>Describes one image locally for inclusion in disclosure-gated context.</summary>
    public async Task<IReadOnlyList<string>> DescribeAsync(
        TurnImageAttachment? attachment,
        CancellationToken cancellationToken)
    {
        if (attachment is null)
        {
            return [];
        }

        Validate(attachment);
        var description = await this.vision.DescribeAsync(
            attachment.Bytes,
            "Describe this image accurately for answering the user's accompanying message.",
            cancellationToken).ConfigureAwait(false);
        return [$"Local vision description of {attachment.FileName}: {description}"];
    }

    private static void Validate(TurnImageAttachment attachment)
    {
        if (!attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The attachment must be an image.");
        }

        if (attachment.Bytes.Length is 0 or > MAX_IMAGE_BYTES)
        {
            throw new ArgumentException("The image must contain 1 byte through 12 MiB.");
        }
    }
}
