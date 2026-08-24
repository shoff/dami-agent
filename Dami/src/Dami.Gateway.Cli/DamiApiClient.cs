using System.Net.Http.Json;
using System.Text.Json;

namespace Dami.Gateway.Cli;

/// <summary>The thin-client transport (D-005): every verb is a call to the runtime API.</summary>
public sealed class DamiApiClient
{
    /// <summary>Where the runtime listens. Localhost is the boundary, not a default.</summary>
    public const string BASE_URL = "http://127.0.0.1:5810";

    private readonly HttpClient httpClient;

    /// <summary>Creates the client.</summary>
    public DamiApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
    }

    /// <summary>GETs a JSON document, or null on 404.</summary>
    public async Task<JsonDocument?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await this.httpClient
            .GetAsync(new Uri(BASE_URL + path), cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>POSTs a JSON body, returning the JSON reply, or null on 404.</summary>
    public async Task<JsonDocument?> PostAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient
            .PostAsJsonAsync(new Uri(BASE_URL + path), body, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>POSTs and streams the server-sent-event response.</summary>
    public async Task<HttpResponseMessage> PostStreamAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BASE_URL + path))
        {
            Content = JsonContent.Create(body),
        };
        return await this.httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<JsonDocument?> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(text);
    }
}

/// <summary>Uniform handling of an unreachable runtime — the thin client's one failure mode.</summary>
public static class ApiCall
{
    /// <summary>Runs an API-backed verb, translating connection failure into advice.</summary>
    public static async Task<int> RunAsync(Func<Task<int>> verb)
    {
        ArgumentNullException.ThrowIfNull(verb);
        try
        {
            return await verb().ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            await Console.Error.WriteLineAsync(
                $"dami-host unreachable at {DamiApiClient.BASE_URL} ({exception.Message})")
                .ConfigureAwait(false);
            await Console.Error.WriteLineAsync("check: systemctl status dami-host").ConfigureAwait(false);
            return 1;
        }
    }
}
