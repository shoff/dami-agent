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
}
