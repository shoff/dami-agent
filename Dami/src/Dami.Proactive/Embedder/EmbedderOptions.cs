namespace Dami.Proactive.Embedder;

/// <summary>The embedder's model name and per-pass ceiling.</summary>
public sealed class EmbedderOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Embedder";

    /// <summary>The interim model per ADR-0009. Versioned into every row it writes.</summary>
    public string EmbeddingModel { get; set; } = "BAAI/bge-m3";

    /// <summary>The most observations one pass will index.</summary>
    public int MaxPerPass { get; set; } = 2000;
}
