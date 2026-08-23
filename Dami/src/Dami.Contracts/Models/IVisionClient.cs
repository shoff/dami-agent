namespace Dami.Contracts.Models;

/// <summary>Local vision — captioning and categorization of personal media.</summary>
/// <remarks>
/// D-012 makes this a requirement rather than a preference: personal photos and their
/// captions are local-only, so the model that looks at them must live on this host.
/// Implementations talk to loopback; an image passed through this interface never
/// leaves the machine.
/// </remarks>
public interface IVisionClient
{
    /// <summary>Answers a question about one image.</summary>
    /// <param name="imageBytes">The image, raw.</param>
    /// <param name="prompt">What to do with it — caption, categorize, describe.</param>
    /// <param name="cancellationToken">Cancels the description.</param>
    Task<string> DescribeAsync(
        ReadOnlyMemory<byte> imageBytes,
        string prompt,
        CancellationToken cancellationToken);
}
