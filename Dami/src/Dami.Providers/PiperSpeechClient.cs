using System.Net.Http.Json;
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
        using var response = await this.httpClient.PostAsJsonAsync(
            new Uri(new Uri(this.piperOptions.BaseUrl), "/speak"),
            new { text, voice = this.piperOptions.Voice },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}
