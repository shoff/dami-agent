namespace Dami.Core.Context;

/// <summary>The context budget — the number this project exists to keep small.</summary>
public sealed class ContextOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Context";

    /// <summary>Hard ceiling on retrieved-context tokens per turn.</summary>
    /// <remarks>
    /// §3.2 targets ~5k for the whole stable prompt; retrieval gets a slice of it.
    /// Hermes carried 90k–126k. This number is the difference between the two systems.
    /// </remarks>
    public int MaxRetrievedTokens { get; set; } = 2500;

    /// <summary>ANN candidates fetched before reranking.</summary>
    public int Candidates { get; set; } = 24;

    /// <summary>Memories kept after reranking, before the budget trims further.</summary>
    public int MaxMemories { get; set; } = 8;

    /// <summary>The subject whose beliefs are included.</summary>
    public string Subject { get; set; } = "steve";

    /// <summary>Memory slots reserved for the most recent relevant items.</summary>
    /// <remarks>
    /// Pure relevance let five-month-old crisis memories fill the whole window and the
    /// model answered as if the crisis were current. Reserving slots for recent items
    /// keeps "what is happening now" in the prompt without abandoning relevance for the
    /// rest. Zero disables the reservation.
    /// </remarks>
    public int RecentSlots { get; set; } = 3;

    /// <summary>How recent "recent" is, in days.</summary>
    public int RecentDays { get; set; } = 30;

    /// <summary>Cosine-distance ceiling: candidates farther than this never enter.</summary>
    /// <remarks>
    /// The grounding gate. Observed failure without it: a question with no relevant
    /// memories still filled the window with the nearest junk, and the model
    /// confabulated an answer from it. Better an explicitly empty context than a
    /// misleading one. On this corpus bge-m3 relevant pairs measured ~0.45–0.55;
    /// tune against the eval set, not by feel.
    /// </remarks>
    public double MaxDistance { get; set; } = 0.62;

    /// <summary>Most beliefs a turn may carry when retrieving by similarity.</summary>
    public int BeliefSlots { get; set; } = 8;

    /// <summary>
    /// Distance gate for beliefs. Calibrated against live bge-m3 vectors on
    /// 2026-08-23: beliefs relevant to a query measured 0.40-0.43, irrelevant
    /// ones 0.63-0.72 — 0.60 splits those bands cleanly. Re-measure if the
    /// embedding model changes.
    /// </summary>
    public double BeliefMaxDistance { get; set; } = 0.60;
}
