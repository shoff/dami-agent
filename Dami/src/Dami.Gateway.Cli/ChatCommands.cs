namespace Dami.Gateway.Cli;

/// <summary>One interactive turn, streamed from the runtime API.</summary>
public sealed class ChatCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public ChatCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>
    /// Runs one turn on the subscription frontier (ADR-0011): Dami's identity and the
    /// question, no retrieved memory. For memory-informed frontier work use `dami brief`,
    /// where the exact bytes are reviewed before anything leaves.
    /// </summary>
    public Task<int> FrontierTurnAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ApiCall.RunAsync(async () =>
        {
            Console.WriteLine("[Frontier · codex subscription · no memories sent]");
            Console.WriteLine();
            using var reply = await this.api.PostAsync(
                "/turns", new { message = request, frontier = true }, cancellationToken)
                .ConfigureAwait(false);
            var root = reply!.RootElement;
            Console.WriteLine(root.GetProperty("answer").GetString());
            Console.WriteLine();
            Console.WriteLine(
                $"replay: dami trace {root.GetProperty("traceId").GetGuid().ToString("N")[..8]}");
            return 0;
        });
    }

    /// <summary>Runs one streaming turn, printing tokens as they arrive.</summary>
    public Task<int> TurnAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ApiCall.RunAsync(async () =>
        {
            using var response = await this.api.PostStreamAsync(
                "/turns/stream", new { message = request }, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            Console.WriteLine(
                $"[{Header(response, "X-Dami-Route")} · ~{Header(response, "X-Dami-Ctx-Tokens")} ctx tokens"
                + $" · {Header(response, "X-Dami-Memories")} memories"
                + $" · {Header(response, "X-Dami-Beliefs")} beliefs]");
            Console.WriteLine();

            await PrintStreamAsync(response, cancellationToken).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"replay: dami trace {Header(response, "X-Dami-Trace")?[..8]}");
            return 0;
        });
    }

    private static async Task PrintStreamAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(body);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                lines.Add(line["data: ".Length..]);
                continue;
            }

            if (line.Length == 0 && lines.Count > 0)
            {
                // One SSE event is one model fragment; embedded newlines arrive as
                // consecutive data lines.
                Console.Write(string.Join('\n', lines));
                lines.Clear();
                await Console.Out.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
