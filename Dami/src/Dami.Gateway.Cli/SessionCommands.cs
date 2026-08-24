using System.Text.Json;

namespace Dami.Gateway.Cli;

/// <summary>Thin-client commands for durable conversation sessions.</summary>
public sealed class SessionCommands
{
    private readonly DamiApiClient api;

    /// <summary>Creates the commands.</summary>
    public SessionCommands(DamiApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Starts a new client-identified session.</summary>
    public async Task<int> StartAsync(string? requestedId, CancellationToken cancellationToken)
    {
        Guid sessionId;
        if (requestedId is null)
        {
            sessionId = Guid.CreateVersion7();
        }
        else if (!TryParseId(requestedId, out sessionId))
        {
            await Console.Error.WriteLineAsync("session id must be a GUID").ConfigureAwait(false);
            return 2;
        }

        return await ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync(
                "/sessions", new { sessionId }, cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                return 1;
            }

            await PrintStateAsync(reply.RootElement).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(false);
    }

    /// <summary>Lists recently active sessions.</summary>
    public Task<int> ListAsync(CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api
                .GetAsync("/sessions", cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                return 1;
            }

            foreach (var session in reply.RootElement.EnumerateArray())
            {
                var id = session.GetProperty("sessionId").GetGuid();
                var state = session.GetProperty("state").GetString();
                var updated = session.GetProperty("updatedAt").GetDateTimeOffset();
                await Console.Out.WriteLineAsync(
                    $"{id:D} {state} {updated:O}")
                    .ConfigureAwait(false);
            }

            return 0;
        });
    }

    /// <summary>Resumes an interrupted session.</summary>
    public Task<int> ResumeAsync(string sessionId, CancellationToken cancellationToken)
    {
        return this.TransitionAsync(sessionId, "resume", cancellationToken);
    }

    /// <summary>Interrupts an active session and its running turn.</summary>
    public Task<int> InterruptAsync(string sessionId, CancellationToken cancellationToken)
    {
        return this.TransitionAsync(sessionId, "interrupt", cancellationToken);
    }

    /// <summary>Runs one turn and prints its reconnect key before the network call.</summary>
    public async Task<int> TurnAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken)
    {
        if (!TryParseId(sessionId, out var parsed) || string.IsNullOrWhiteSpace(message))
        {
            await Console.Error.WriteLineAsync("session id and message are required")
                .ConfigureAwait(false);
            return 2;
        }

        var requestId = Guid.CreateVersion7();
        await Console.Out.WriteLineAsync($"request {requestId:D}").ConfigureAwait(false);
        return await this.PostTurnAsync(
            parsed, requestId, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the durable state of a previously announced request.</summary>
    public async Task<int> ReconnectAsync(
        string sessionId,
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!TryParseId(sessionId, out var session)
            || !TryParseId(requestId, out var request))
        {
            await Console.Error.WriteLineAsync("session id and request id must be GUIDs")
                .ConfigureAwait(false);
            return 2;
        }

        return await ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.GetAsync(
                $"/sessions/{session:D}/turns/{request:D}", cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync("request not found").ConfigureAwait(false);
                return 1;
            }

            await PrintStoredTurnAsync(reply.RootElement).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(false);
    }

    private Task<int> PostTurnAsync(
        Guid sessionId,
        Guid requestId,
        string message,
        CancellationToken cancellationToken)
    {
        return ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync(
                $"/sessions/{sessionId:D}/turns", new { requestId, message }, cancellationToken)
                .ConfigureAwait(false);
            if (reply is null)
            {
                return 1;
            }

            await PrintTurnAsync(reply.RootElement, sessionId, requestId).ConfigureAwait(false);
            return 0;
        });
    }

    private static async Task PrintTurnAsync(
        JsonElement outcome,
        Guid sessionId,
        Guid requestId)
    {
        var turn = outcome.GetProperty("turn");
        var wasReplay = outcome.GetProperty("wasReplay").GetBoolean();
        await PrintStoredTurnAsync(turn).ConfigureAwait(false);

        await Console.Out.WriteLineAsync(
            $"reconnect: dami session reconnect {sessionId:D} {requestId:D}")
            .ConfigureAwait(false);
        if (wasReplay)
        {
            await Console.Out.WriteLineAsync("(durable replay; not re-executed)").ConfigureAwait(false);
        }
    }

    private static async Task PrintStoredTurnAsync(JsonElement turn)
    {
        var traceId = turn.GetProperty("traceId").GetGuid();
        var state = turn.GetProperty("state").GetString();
        await Console.Out.WriteLineAsync($"{state} · trace {traceId:N}").ConfigureAwait(false);
        if (turn.GetProperty("response").GetString() is { } response)
        {
            await Console.Out.WriteLineAsync($"Dami: {response}").ConfigureAwait(false);
        }
    }

    private async Task<int> TransitionAsync(
        string sessionId,
        string action,
        CancellationToken cancellationToken)
    {
        if (!TryParseId(sessionId, out var parsed))
        {
            await Console.Error.WriteLineAsync("session id must be a GUID").ConfigureAwait(false);
            return 2;
        }

        return await ApiCall.RunAsync(async () =>
        {
            using var reply = await this.api.PostAsync(
                $"/sessions/{parsed:D}/{action}", new { }, cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                await Console.Error.WriteLineAsync("session not found").ConfigureAwait(false);
                return 1;
            }

            await PrintStateAsync(reply.RootElement).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(false);
    }

    private static Task PrintStateAsync(JsonElement session)
    {
        var id = session.GetProperty("sessionId").GetGuid();
        var state = session.GetProperty("state").GetString();
        return Console.Out.WriteLineAsync($"session {id:D} {state}");
    }

    private static bool TryParseId(string value, out Guid parsed)
    {
        return Guid.TryParse(value, out parsed) && parsed != Guid.Empty;
    }
}
