namespace Dami.Contracts.Models;

/// <summary>Text to speech on the host. The audio never leaves it unless a caller sends it.</summary>
public interface ISpeechClient
{
    /// <summary>Renders <paramref name="text"/> as WAV bytes.</summary>
    Task<byte[]> SpeakAsync(string text, CancellationToken cancellationToken);

    /// <summary>The voice in use, for the trace.</summary>
    string VoiceId { get; }
}
