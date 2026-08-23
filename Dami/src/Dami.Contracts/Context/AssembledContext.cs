namespace Dami.Contracts.Context;

/// <summary>Everything a turn's prompt is built from, with its cost accounted.</summary>
/// <remarks>
/// The measured Hermes failure was 90k–126k tokens per request; the §3.2 target is a
/// ~5k stable prompt and lean retrieval. This type carries its own estimated token
/// count so the budget is enforced where assembly happens, not audited after the fact.
/// </remarks>
public sealed record AssembledContext
{
    /// <summary>Creates an assembled context.</summary>
    public AssembledContext(
        IReadOnlyList<RetrievedItem> memories,
        IReadOnlyList<RetrievedItem> beliefs,
        int estimatedTokens)
    {
        ArgumentNullException.ThrowIfNull(memories);
        ArgumentNullException.ThrowIfNull(beliefs);

        this.Memories = memories;
        this.Beliefs = beliefs;
        this.EstimatedTokens = estimatedTokens;
    }

    /// <summary>Relevant observations, most relevant first.</summary>
    public IReadOnlyList<RetrievedItem> Memories { get; }

    /// <summary>Active conclusions about the subject.</summary>
    public IReadOnlyList<RetrievedItem> Beliefs { get; }

    /// <summary>Rough token cost of everything above.</summary>
    public int EstimatedTokens { get; }
}
