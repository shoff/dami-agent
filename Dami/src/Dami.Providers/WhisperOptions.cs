namespace Dami.Providers;

/// <summary>The local speech-to-text sidecar (L3).</summary>
public sealed class WhisperOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Whisper";

    /// <summary>Loopback address of the sidecar. Never a remote host.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8090";

    /// <summary>The model the sidecar serves.</summary>
    public string Model { get; set; } = "Systran/faster-whisper-small.en";
}
