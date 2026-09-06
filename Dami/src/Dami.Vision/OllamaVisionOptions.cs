namespace Dami.Vision;

/// <summary>Where the vision-capable sidecar listens, and which model to use.</summary>
public sealed class OllamaVisionOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Vision";

    /// <summary>The sidecar's base address. Loopback by design — images never leave.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>The vision model.</summary>
    public string Model { get; set; } = "qwen2.5vl:7b";

    /// <summary>Cap on generated tokens per description.</summary>
    public int MaxTokens { get; set; } = 300;

    /// <summary>
    /// The context window asked for per caption. Ollama's default is 4,096 tokens, and
    /// qwen2.5-vl spends up to 4,096 of them on the picture alone (its ceiling of
    /// 3,211,264 pixels at 28×28 per token): a phone photo plus the gym prompt came to
    /// 4,131 and the sidecar refused it with 400 "exceeds the available context size" —
    /// four gym photos in a row, 2026-09-05 to 09-06, every one "could not be read".
    /// </summary>
    public int ContextTokens { get; set; } = 8192;

    /// <summary>
    /// How long Ollama keeps the vision model loaded after a caption, in seconds. Zero
    /// unloads it at once: on a 16 GiB card it cannot sit beside the text model and the
    /// embedders, and a model left half on the CPU is what the LLM guard restarts.
    /// </summary>
    public int KeepAliveSeconds { get; set; }

    /// <summary>
    /// Models to unload before a caption, so the vision model gets the whole card. With
    /// the text model resident and pinned, Ollama pushed it to the CPU to make room and it
    /// never came back (2026-09-05 21:50: qwen3 "100% CPU, Forever", every local call
    /// crawling, the image decode failing with 400). Unloaded first, it reloads on the
    /// GPU for the next text call at the cost of a few seconds.
    /// </summary>
    public IList<string> EvictFirst { get; } = ["qwen3:8b"];
}
