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
}
