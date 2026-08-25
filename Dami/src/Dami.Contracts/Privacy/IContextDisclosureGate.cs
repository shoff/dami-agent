namespace Dami.Contracts.Privacy;

/// <summary>What may be done with one piece of retrieved context before it egresses.</summary>
public enum Disclosure
{
    /// <summary>Send it as it stands. Nothing about it identifies Steve or anyone else.</summary>
    Pass,

    /// <summary>
    /// The fact is needed to answer but the identity is not. Send it attributed to an
    /// unnamed third party — "a friend has…", "someone I know…" — so the frontier can
    /// reason about the situation without being told whose it is.
    /// </summary>
    Disguise,

    /// <summary>Too personal to leave, and not needed enough to justify it.</summary>
    Withhold,
}

/// <summary>One classified piece of context, with the text that would actually be sent.</summary>
public sealed record DisclosedItem(string Original, Disclosure Disclosure, string Sendable, string Reason);

/// <summary>
/// Decides, locally, what of Steve's retrieved memory may reach a frontier model.
/// </summary>
/// <remarks>
/// Withholding everything makes the frontier useless; sending everything makes D-012 a
/// slogan. The gate exists to make that a per-item judgement rather than a single
/// blanket setting — and crucially to have a third answer, because a fact is often
/// needed while the identity attached to it is not.
///
/// It runs on the local sidecar and its decisions are recorded, so Steve can correct
/// them; the corrections become examples, and the gate is expected to get better at his
/// particular boundaries over time rather than at boundaries in general.
/// </remarks>
public interface IContextDisclosureGate
{
    /// <summary>Classifies each retrieved item for this question.</summary>
    Task<IReadOnlyList<DisclosedItem>> ClassifyAsync(
        string question,
        IReadOnlyList<string> context,
        CancellationToken cancellationToken);
}
