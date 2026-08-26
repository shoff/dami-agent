namespace Dami.Providers;

/// <summary>Where the local Piper sidecar listens and which voice it speaks with.</summary>
public sealed class PiperOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Piper";

    /// <summary>Loopback only.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8091";

    /// <summary>The voice, installed deliberately and recorded with its licence (L4).</summary>
    public string Voice { get; set; } = "en_US-ljspeech-medium";
}
