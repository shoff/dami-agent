namespace Dami.Contracts.Models;

/// <summary>The second door through the boundary: frontier model completion (ADR-0010).</summary>
/// <remarks>
/// Deliberately separate from the bodyless <c>IEgressClient</c> — a chat call is all
/// body, and sharing one door would hand every fetch-capable service a payload channel.
/// Implementations enforce rather than trust: non-Egressable prompts are refused even
/// though the router should make that unreachable.
/// </remarks>
public interface IFrontierChat
{
    /// <summary>Completes a prompt at a frontier provider.</summary>
    /// <exception cref="Privacy.EgressRefusedException">
    /// The prompt is not Egressable, or the provider host is not allowlisted.
    /// </exception>
    Task<string> CompleteAsync(FrontierPrompt prompt, CancellationToken cancellationToken);

    /// <summary>Completes, yielding the answer as it arrives.</summary>
    /// <remarks>
    /// The same door and the same refusals; only the shape of the reply differs. A
    /// provider whose transport cannot stream implements this by completing and yielding
    /// once, which keeps callers from having to ask which kind they hold.
    /// </remarks>
    /// <exception cref="Privacy.EgressRefusedException">
    /// The prompt is not Egressable, or the provider host is not allowlisted.
    /// </exception>
    IAsyncEnumerable<string> StreamAsync(FrontierPrompt prompt, CancellationToken cancellationToken);

    /// <summary>Streams a prompt with an image supplied directly to the frontier.</summary>
    IAsyncEnumerable<string> StreamAsync(
        FrontierPrompt prompt,
        IReadOnlyList<FrontierImage> images,
        CancellationToken cancellationToken) =>
        images.Count == 0
            ? this.StreamAsync(prompt, cancellationToken)
            : throw new NotSupportedException("This frontier does not accept image input.");

    /// <summary>
    /// Streams an answer while offering the frontier a bundle of tools it may call on
    /// this host mid-turn. A frontier that cannot host tools refuses a non-empty bundle
    /// rather than silently answering without it.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        FrontierPrompt prompt,
        IReadOnlyList<FrontierImage> images,
        FrontierToolbox tools,
        CancellationToken cancellationToken) =>
        tools.IsEmpty
            ? this.StreamAsync(prompt, images, cancellationToken)
            : throw new NotSupportedException("This frontier does not host tools.");
}

/// <summary>An image the user explicitly supplied to a frontier conversation.</summary>
public sealed record FrontierImage(string FileName, string ContentType, ReadOnlyMemory<byte> Bytes);
