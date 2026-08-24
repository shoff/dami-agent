using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>The Ollama sidecar as an <see cref="IChatClient"/>.</summary>
public sealed class OllamaChatClient : IChatClient
{
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
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
        this.baseUri = LocalSidecarEndpoint.Parse(this.ollamaOptions.BaseUrl, nameof(ollamaOptions));
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var endpoint = new Uri(this.baseUri, "/api/generate");
        var request = new
        {
            model = this.ollamaOptions.Model,
            prompt,
            think = this.ollamaOptions.Think,
            stream = false,
            options = new { num_predict = this.ollamaOptions.MaxTokens },
        };

        using var response = await this.httpClient
            .PostAsJsonAsync(endpoint, request, OllamaJson.SerializerOptions, cancellationToken)
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

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        using var response = await this.OpenStreamAsync(prompt, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var fragment = ParseFragment(line);
            if (!string.IsNullOrEmpty(fragment))
            {
                yield return fragment;
            }
        }
    }

    private async Task<HttpResponseMessage> OpenStreamAsync(string prompt, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(new Uri(this.ollamaOptions.BaseUrl), "/api/generate");
        var request = new
        {
            model = this.ollamaOptions.Model,
            prompt,
            think = this.ollamaOptions.Think,
            stream = true,
            options = new { num_predict = this.ollamaOptions.MaxTokens },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(request, options: OllamaJson.SerializerOptions),
        };
        var response = await this.httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response;
    }

    /// <summary>Extracts the answer fragment from one stream line. Thinking is skipped.</summary>
    private static string? ParseFragment(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        using var document = JsonDocument.Parse(line);
        return document.RootElement.TryGetProperty("response", out var found)
            ? found.GetString()
            : null;
    }
}
