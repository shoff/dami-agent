namespace Dami.Contracts.Models;

/// <summary>Speech to text, on the host (L3). Audio never leaves the machine.</summary>
/// <remarks>
/// The same posture as vision: a local sidecar, loopback only, no egress path. Spoken
/// input is at least as personal as anything in the corpus.
/// </remarks>
public interface ITranscriptionClient
{
    /// <summary>Transcribes one audio clip.</summary>
    Task<string> TranscribeAsync(
        byte[] audio,
        string fileName,
        CancellationToken cancellationToken);

    /// <summary>The model doing the transcription, for provenance.</summary>
    string ModelId { get; }
}
