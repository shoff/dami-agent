using System.Text.Json;
using Dami.Contracts.Models;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>Speaks through the local Piper sidecar over loopback (L4).</summary>
/// <remarks>Swapping the engine means changing this adapter and nothing above it.</remarks>
public sealed class PiperSpeechClient : ISpeechClient
{
    private readonly HttpClient httpClient;
    private readonly PiperOptions piperOptions;

    /// <summary>Creates the client.</summary>
    public PiperSpeechClient(HttpClient httpClient, IOptions<PiperOptions> piperOptions)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(piperOptions);
        this.httpClient = httpClient;
        this.piperOptions = piperOptions.Value;
    }

    /// <inheritdoc />
    public string VoiceId => this.piperOptions.Voice;

    /// <inheritdoc />
    public async Task<byte[]> SpeakAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        // A plain body with a Content-Length: the sidecar is Python's http.server, which
        // does not read chunked requests, and PostAsJsonAsync would send one.
        using var body = new StringContent(
            JsonSerializer.Serialize(new { text, voice = this.piperOptions.Voice }),
            System.Text.Encoding.UTF8, "application/json");
        using var response = await this.httpClient.PostAsync(
            new Uri(new Uri(this.piperOptions.BaseUrl), "/speak"), body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}
