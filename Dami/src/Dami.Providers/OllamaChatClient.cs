using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>The Ollama sidecar as an <see cref="IChatClient"/>.</summary>
public sealed class OllamaChatClient : IChatClient
{
    private static readonly JsonSerializerOptions serializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly OllamaOptions ollamaOptions;
    private readonly ILogger<OllamaChatClient> logger;

    /// <summary>Creates the client.</summary>
    public OllamaChatClient(
        HttpClient httpClient,
        IOptions<OllamaOptions> ollamaOptions,
        ILogger<OllamaChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(ollamaOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.ollamaOptions = ollamaOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var endpoint = new Uri(new Uri(this.ollamaOptions.BaseUrl), "/api/generate");
        var request = new
        {
            model = this.ollamaOptions.Model,
            prompt,
            think = this.ollamaOptions.Think,
            stream = false,
            options = new { num_predict = this.ollamaOptions.MaxTokens },
        };

        using var response = await this.httpClient
            .PostAsJsonAsync(endpoint, request, serializerOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var text = body.RootElement.TryGetProperty("response", out var found)
            ? found.GetString() ?? string.Empty
            : string.Empty;

        this.logger.LogDebug("Ollama completed {Chars} characters", text.Length);
        return text;
    }
}
