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

    /// <summary>POSTs raw bytes (audio, images) and returns the JSON reply.</summary>
    public async Task<JsonDocument?> PostBytesAsync(
        string path,
        byte[] payload,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(contentType);

        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var response = await this.httpClient
            .PostAsync(new Uri(BASE_URL + path), content, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Turns a failed response into a named failure. The streaming path cannot go through
    /// <see cref="ReadAsync"/>, and calling EnsureSuccessStatusCode there is what made a
    /// stopped sidecar report as an unreachable host.
    /// </summary>
    public static async Task ThrowIfFailedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new Dami.Contracts.Privacy.EgressRefusedException(ReasonFrom(body));
        }

        throw new DamiRuntimeException(DetailFrom(body, response.StatusCode));
    }

    private static string DetailFrom(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return error.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body.
        }

        return body.Length > 0 ? body : $"the runtime returned {(int)status}";
    }

    private static string ReasonFrom(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("refused", out var refused)
                ? refused.GetString() ?? body
                : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static async Task<JsonDocument?> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        // The runtime refusing on privacy grounds is a real answer; reporting it as a
        // transport failure sends the reader to the wrong problem entirely.
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var refusal = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new Dami.Contracts.Privacy.EgressRefusedException(ReasonFrom(refusal));
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new DamiRuntimeException(DetailFrom(body, response.StatusCode));
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(text);
    }
}

/// <summary>The runtime answered, and the answer was a failure. Distinct from being unable
/// to reach it at all — conflating the two sends the reader to the wrong problem.</summary>
public sealed class DamiRuntimeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DamiRuntimeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public DamiRuntimeException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public DamiRuntimeException(string message, Exception innerException)
        : base(message, innerException)
    {
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
        catch (DamiRuntimeException failure)
        {
            await Console.Error.WriteLineAsync($"the runtime failed: {failure.Message}")
                .ConfigureAwait(false);
            await Console.Error.WriteLineAsync(
                "the trace records the cause: dami trace <id>, or journalctl -u dami-host")
                .ConfigureAwait(false);
            return 1;
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
