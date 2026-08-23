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
}
