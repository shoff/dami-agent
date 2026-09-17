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

    /// <summary>
    /// Cap on generated tokens per completion. A ceiling, not a target: generation stops
    /// at the model's end token. Raised from 1,200 on 2026-09-06 after the disclosure
    /// gate's verdict on 36 items was cut off at exactly 1,200 and the turn lost its
    /// whole history.
    /// </summary>
    public int MaxTokens { get; set; } = 2400;

    /// <summary>
    /// The context window asked for per request, in tokens.
    /// </summary>
    /// <remarks>
    /// Without this the sidecar uses its own default, and on this host that was 2,050
    /// tokens per slot: <c>docker logs dami-llm</c> held 122 "truncating input prompt"
    /// warnings in the 14 days to 2026-09-16, every one <c>keep=4</c> — the instructions
    /// at the head of the prompt were the first thing dropped. The weekly reflection of
    /// 2026-09-13 sent 332,874 tokens and the model saw a random 2,050 of them, which is
    /// why it "proposed nothing" twice. qwen3:8b supports 40,960; 12,288 costs about
    /// 1.7 GiB of KV cache on top of the weights and leaves room for the vision model
    /// under the 16 GiB card. The vision client pins its own window the same way.
    /// </remarks>
    public int ContextTokens { get; set; } = 12288;

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

