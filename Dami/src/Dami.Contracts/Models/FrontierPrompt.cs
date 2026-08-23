using Dami.Contracts.Context;
using Dami.Contracts.Events;

namespace Dami.Contracts.Models;

/// <summary>A prompt bound for a frontier provider, with its boundary paperwork.</summary>
/// <remarks>
/// ADR-0010: the privacy class rides with the prompt so the adapter can refuse rather
/// than trust its caller, and the trace fields put the egress events where they belong.
/// The purpose line is what appears in event labels — the prompt text never does.
/// </remarks>
public sealed record FrontierPrompt
{
    /// <summary>Creates a frontier prompt.</summary>
    public FrontierPrompt(
        string prompt,
        string purpose,
        PrivacyClass privacy,
        Guid traceId,
        ExecutionOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(purpose);

        this.Prompt = prompt;
        this.Purpose = purpose;
        this.Privacy = privacy;
        this.TraceId = traceId;
        this.Origin = origin;
    }

    /// <summary>The text sent to the provider.</summary>
    public string Prompt { get; }

    /// <summary>One human-readable line for the event trail. Never contains the prompt.</summary>
    public string Purpose { get; }

    /// <summary>The class the prompt was assembled under. Anything but Egressable is refused.</summary>
    public PrivacyClass Privacy { get; }

    /// <summary>The trace the egress events join.</summary>
    public Guid TraceId { get; }

    /// <summary>What kind of work is reaching out.</summary>
    public ExecutionOrigin Origin { get; }
}
