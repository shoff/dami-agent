namespace Dami.Providers;

/// <summary>Where the local Piper sidecar listens and which voice it speaks with.</summary>
public sealed class PiperOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Piper";

    /// <summary>Loopback only.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8091";

    /// <summary>The voice, installed deliberately and recorded with its licence (L4).</summary>
    /// <remarks>
    /// This is the setting that decides what Dami sounds like, and the sidecar's
    /// <c>DAMI_TTS_VOICE</c> is not: the client sends an explicit voice on every request,
    /// so the sidecar's own default is only ever used by something calling it directly.
    /// Changing the systemd unit alone would have looked like a fix and changed nothing.
    ///
    /// steve-clean is Steve's own voice, trained on this host from his recordings with the
    /// vocal stem isolated first. ADR-0022 chose LJ Speech for legal cleanliness; that
    /// reasoning was about using someone else's voice and does not apply to his. The
    /// LJ Speech voice stays installed and selectable.
    /// </remarks>
    public string Voice { get; set; } = "steve-clean";
}
