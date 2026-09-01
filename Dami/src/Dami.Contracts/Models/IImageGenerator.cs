using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;

namespace Dami.Contracts.Models;

/// <summary>A request to render an image, carrying what the boundary needs to judge it.</summary>
/// <remarks>
/// Shaped like <see cref="FrontierPrompt"/> deliberately. An image request is the same
/// kind of thing — all body, going to a provider, answerable only by them — so it carries
/// the same privacy class, trace and origin, and is refused on the same terms.
/// </remarks>
public sealed record ImageRequest
{
    /// <summary>Creates an image request.</summary>
    public ImageRequest(
        string prompt,
        string purpose,
        PrivacyClass privacy,
        Guid traceId,
        ExecutionOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(purpose);

        this.Prompt = prompt;
        this.Purpose = purpose;
        this.Privacy = privacy;
        this.TraceId = traceId;
        this.Origin = origin;
    }

    /// <summary>What to draw.</summary>
    public string Prompt { get; }

    /// <summary>Why, for the event stream. Never the prompt itself.</summary>
    public string Purpose { get; }

    /// <summary>Whether this may leave the host at all.</summary>
    public PrivacyClass Privacy { get; }

    /// <summary>The trace this belongs to.</summary>
    public Guid TraceId { get; }

    /// <summary>What kind of work asked for it.</summary>
    public ExecutionOrigin Origin { get; }

    /// <summary>Pixel size, in the provider's vocabulary.</summary>
    public string Size { get; init; } = "1024x1536";

    /// <summary>Render quality, in the provider's vocabulary.</summary>
    public string Quality { get; init; } = "high";
}

/// <summary>A generated image and what it was asked to be.</summary>
public sealed record GeneratedImage(
    string FileName,
    ReadOnlyMemory<byte> Bytes,
    string ContentType,
    string Prompt);

/// <summary>Makes an image from a description. The third door through the boundary.</summary>
/// <remarks>
/// Separate from <see cref="IEgressClient"/>, which is bodyless by design, and from
/// <see cref="IFrontierChat"/>, which returns prose: this one sends a body and returns
/// bytes. Implementations enforce rather than trust — a non-Egressable prompt is refused,
/// the provider host must be on the same allowlist as every other destination, and every
/// call lands in the event stream with its purpose and never its prompt text.
///
/// This costs money per call (ADR-0027), which is why the scheduled caller is bounded and
/// why an absent key is treated as absent capability rather than an error to retry.
/// </remarks>
public interface IImageGenerator
{
    /// <summary>Renders a request.</summary>
    /// <exception cref="EgressRefusedException">
    /// The prompt is not Egressable, the host is not allowlisted, or no key is configured.
    /// </exception>
    Task<GeneratedImage> GenerateAsync(ImageRequest request, CancellationToken cancellationToken);
}
