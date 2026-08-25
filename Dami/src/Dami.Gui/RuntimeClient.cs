using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Dami.Gui;

/// <summary>Talks to the localhost runtime API (D-005) — the same surface the CLI uses.</summary>
/// <remarks>
/// The desktop client is a thin client like every other: it renders what the runtime
/// persisted and asks the runtime to act. It holds no database connection and no model
/// credentials of its own.
/// </remarks>
public sealed class RuntimeClient
{
    /// <summary>Where the runtime listens. Loopback is a privacy boundary, not a default.</summary>
    public const string BASE_URL = "http://127.0.0.1:5810";

    // UseProxy=false is load-bearing, not tidiness. Inside a desktop session .NET's
    // system-proxy detection can block on the very first request, and the symptom is
    // silent: no socket is ever opened, the await never returns, and the window sits
    // there looking like an idle system with nothing to show.
    private readonly HttpClient httpClient = new(
        new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    /// <summary>Reads a JSON array from the runtime, or an empty document on failure.</summary>
    public async Task<JsonDocument?> GetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await this.httpClient
                .GetAsync(new Uri(BASE_URL + path), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            return JsonDocument.Parse(text);
        }
        catch (Exception exception)
        {
            Diagnostics.Write($"GET {path} FAILED {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>Posts JSON and returns the reply, or null when the runtime is unreachable.</summary>
    public async Task<JsonDocument?> PostAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await this.httpClient
                .PostAsJsonAsync(new Uri(BASE_URL + path), body, cancellationToken)
                .ConfigureAwait(false);
            return JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Opens the stream, raising a named failure rather than yielding silence.</summary>
    private async Task<HttpResponseMessage> OpenStreamAsync(
        string message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BASE_URL + "/turns/stream"))
        {
            Content = JsonContent.Create(new { message }),
        };
        var response = await this.httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        response.Dispose();
        throw new HttpRequestException($"the runtime returned {(int)response.StatusCode} {detail}");
    }

    /// <summary>Streams one turn's answer fragment by fragment as the model produces it.</summary>
    public async IAsyncEnumerable<string> StreamTurnAsync(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await this.OpenStreamAsync(message, cancellationToken)
            .ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(body);
        var pending = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                pending.Add(line["data: ".Length..]);
                continue;
            }

            if (line.Length == 0 && pending.Count > 0)
            {
                yield return string.Join('\n', pending);
                pending.Clear();
            }
        }
    }
}
