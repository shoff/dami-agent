namespace Dami.Proactive.Reflection;

/// <summary>The reflection pass's floors and ceilings.</summary>
public sealed class ReflectionOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Reflection";

    /// <summary>Fewer observations than this and the pass stays quiet.</summary>
    public int MinimumObservations { get; set; } = 3;

    /// <summary>The most observations one prompt carries.</summary>
    public int MaximumObservations { get; set; } = 100;

    /// <summary>A proposal below this confidence is discarded.</summary>
    public double MinimumConfidence { get; set; } = 0.5;

    /// <summary>How many semantically related older observations join the prompt.</summary>
    /// <remarks>Zero disables retrieval augmentation.</remarks>
    public int RelatedObservations { get; set; } = 5;

    /// <summary>Health-timeline rows joined into the reflection prompt (K3, D-007).</summary>
    public int HealthTimelineRows { get; set; } = 20;
}

