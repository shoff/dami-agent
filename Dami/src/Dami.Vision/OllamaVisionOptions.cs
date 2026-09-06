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
