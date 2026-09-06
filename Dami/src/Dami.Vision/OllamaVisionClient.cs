using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Vision;

/// <summary>Vision through the loopback Ollama sidecar.</summary>
public sealed class OllamaVisionClient : IVisionClient
{
    private static readonly JsonSerializerOptions serializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly OllamaVisionOptions visionOptions;
    private readonly ILogger<OllamaVisionClient> logger;

    /// <summary>Creates the client.</summary>
    public OllamaVisionClient(
        HttpClient httpClient,
        IOptions<OllamaVisionOptions> visionOptions,
        ILogger<OllamaVisionClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(visionOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.visionOptions = visionOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> DescribeAsync(
        ReadOnlyMemory<byte> imageBytes,
        string prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var endpoint = new Uri(new Uri(this.visionOptions.BaseUrl), "/api/generate");
        await this.EvictAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var request = new
        {
            model = this.visionOptions.Model,
            prompt,
            images = new[] { Convert.ToBase64String(imageBytes.Span) },
            stream = false,
            keep_alive = this.visionOptions.KeepAliveSeconds,
            options = new { num_predict = this.visionOptions.MaxTokens, num_ctx = this.visionOptions.ContextTokens },
        };

        using var response = await this.httpClient
            .PostAsJsonAsync(endpoint, request, serializerOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var text = body.RootElement.TryGetProperty("response", out var found)
            ? found.GetString() ?? string.Empty
            : string.Empty;

        this.logger.LogDebug("Vision described {Bytes} bytes in {Chars} chars", imageBytes.Length, text.Length);
        return text.Trim();
    }

    /// <summary>Hands the card over: each listed model is asked to unload before the vision model loads.</summary>
    private async Task EvictAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        foreach (var model in this.visionOptions.EvictFirst)
        {
            try
            {
                using var response = await this.httpClient
                    .PostAsJsonAsync(endpoint, new { model, keep_alive = 0 }, serializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                this.logger.LogInformation("Vision: asked Ollama to unload {Model} first ({Status})", model, (int)response.StatusCode);
            }
            catch (HttpRequestException exception)
            {
                // Not fatal: the caption may still work, just on a crowded card.
                this.logger.LogWarning(exception, "Vision: could not unload {Model} first", model);
            }
        }
    }
}
