using System.Net.Http.Headers;
using Dami.Contracts.Models;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>Transcribes against the local faster-whisper sidecar over loopback.</summary>
/// <remarks>
/// The sidecar speaks the OpenAI transcription shape, which is a convenience of the
/// image and not a dependency on OpenAI — nothing leaves the host. Swapping the engine
/// means changing this adapter and nothing above it.
/// </remarks>
public sealed class WhisperTranscriptionClient : ITranscriptionClient
{
    private readonly HttpClient httpClient;
    private readonly WhisperOptions whisperOptions;

    /// <summary>Creates the client.</summary>
    public WhisperTranscriptionClient(HttpClient httpClient, IOptions<WhisperOptions> whisperOptions)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(whisperOptions);

        this.httpClient = httpClient;
        this.whisperOptions = whisperOptions.Value;
    }

    /// <inheritdoc />
    public string ModelId => this.whisperOptions.Model;

    /// <inheritdoc />
    public async Task<string> TranscribeAsync(
        byte[] audio,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(fileName);
        if (audio.Length == 0)
        {
            throw new ArgumentException("There is no audio to transcribe.", nameof(audio));
        }

        using var form = new MultipartFormDataContent();
        var clip = new ByteArrayContent(audio);
        clip.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(clip, "file", fileName);
        form.Add(new StringContent(this.whisperOptions.Model), "model");
        form.Add(new StringContent("json"), "response_format");

        var endpoint = new Uri(
            new Uri(this.whisperOptions.BaseUrl), "/v1/audio/transcriptions");
        using var response = await this.httpClient
            .PostAsync(endpoint, form, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return document.RootElement.TryGetProperty("text", out var text)
            ? text.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }
}
