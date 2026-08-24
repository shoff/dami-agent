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

    /// <summary>
    /// Seconds the sidecar keeps the model resident: <c>-1</c> never unloads (the
    /// default here, deliberately), <c>0</c> unloads immediately.
    /// </summary>
    /// <remarks>
    /// The sidecar's own default unloads after about five minutes idle, and each
    /// reload is a fresh chance to land on CPU when the embedding and rerank services
    /// hold VRAM. That silent fallback has bitten this host repeatedly: the answer
    /// still arrives, just at a few tokens a second, so it reads as a hang rather than
    /// a failure. Pinning the model removes the reload, and with it the window.
    /// </remarks>
    public int KeepAliveSeconds { get; set; } = -1;
}

