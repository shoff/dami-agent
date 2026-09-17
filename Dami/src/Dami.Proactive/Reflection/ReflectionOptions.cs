namespace Dami.Proactive.Reflection;

/// <summary>The reflection pass's floors and ceilings.</summary>
public sealed class ReflectionOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Reflection";

    /// <summary>Fewer observations than this and the pass stays quiet.</summary>
    public int MinimumObservations { get; set; } = 3;

    /// <summary>How many facts from the shared domain store join the reflection prompt.</summary>
    public int DomainFactRows { get; set; } = 30;

    /// <summary>The most observations one prompt carries.</summary>
    public int MaximumObservations { get; set; } = 100;

    /// <summary>
    /// Characters of one observation's body that reach the prompt; longer bodies are
    /// cut with an ellipsis.
    /// </summary>
    /// <remarks>
    /// A <c>chat</c> observation averaged 5,539 characters on 2026-09-16 (the GUI tabs
    /// record their whole context dump as the request), so a hundred of them is a
    /// prompt no local model can hold. A pattern about the person shows in the first
    /// few hundred characters or it is not in that note.
    /// </remarks>
    public int MaximumObservationCharacters { get; set; } = 600;

    /// <summary>
    /// Characters the whole prompt may reach. Observations are listed in order until
    /// the next one would cross it; the instructions, the believed set and the domain
    /// timeline always fit first.
    /// </summary>
    /// <remarks>
    /// 30,000 characters is roughly 8–10k tokens, inside the 12,288-token window
    /// <c>OllamaOptions.ContextTokens</c> asks for, with the answer's 2,400 tokens to
    /// spare. The 2026-09-13 pass sent 332,874 tokens and the sidecar kept 2,050 of them.
    /// </remarks>
    public int MaximumPromptCharacters { get; set; } = 30000;

    /// <summary>A proposal below this confidence is discarded.</summary>
    public double MinimumConfidence { get; set; } = 0.5;

    /// <summary>How many semantically related older observations join the prompt.</summary>
    /// <remarks>Zero disables retrieval augmentation.</remarks>
    public int RelatedObservations { get; set; } = 5;

    /// <summary>Health-timeline rows joined into the reflection prompt (K3, D-007).</summary>
    public int HealthTimelineRows { get; set; } = 20;
}

