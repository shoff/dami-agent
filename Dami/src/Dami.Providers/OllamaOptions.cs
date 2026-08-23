namespace Dami.Providers;

/// <summary>Where the local Ollama sidecar listens, and which model to use.</summary>
public sealed class OllamaOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Ollama";

    /// <summary>The sidecar's base address. Loopback by design.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>The model to complete with.</summary>
    public string Model { get; set; } = "qwen3:8b";

    /// <summary>
    /// Whether the model's reasoning mode is enabled.
    /// </summary>
    /// <remarks>
    /// Defaults to true because it changes correctness, not just latency: measured on
    /// this workstation, qwen3:8b misclassified with thinking off (0.02 s) and was
    /// correct with it on (3.3 s). The runbook §5 has the numbers. Proactive work is
    /// throughput-tolerant, so the seconds are affordable.
    /// </remarks>
    public bool Think { get; set; } = true;

    /// <summary>Cap on generated tokens per completion.</summary>
    public int MaxTokens { get; set; } = 1200;
}
