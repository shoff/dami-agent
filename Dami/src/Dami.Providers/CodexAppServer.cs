using System.Diagnostics;
using System.Text.Json;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Dami.Providers;

/// <summary>A persistent codex app-server, spoken to over stdio JSON-RPC.</summary>
/// <remarks>
/// The subscription's streaming transport. <c>codex exec</c> writes its answer to a file
/// and is readable only after the process exits, which is why the first implementation
/// could not stream; <c>codex app-server</c> is the protocol the interactive CLI itself
/// uses, and it emits <c>item/agentMessage/delta</c> notifications token by token.
///
/// One process, kept alive, rather than one per turn: the spawn was a measurable part of
/// every frontier call. One turn at a time, because this is a single-user runtime and
/// interleaving two turns on one stdio pipe buys nothing but ordering bugs.
///
/// A fresh thread per turn, deliberately. Reusing a thread would leave the previous turn's
/// gated context in codex's own history, so the second turn would disclose what the gate
/// approved for the first. Each turn sends exactly what was approved for it and nothing
/// else — which is also what <c>codex exec</c> did.
/// </remarks>
public interface ICodexAppServer
{
    /// <summary>Runs one turn, yielding the answer as it arrives.</summary>
    IAsyncEnumerable<string> StreamAsync(
        string prompt, string workingDirectory, TimeSpan timeout, IReadOnlyList<string> imagePaths,
        FrontierToolbox tools, CancellationToken cancellationToken);
}

/// <summary>The real app-server process.</summary>
public sealed class CodexAppServer : ICodexAppServer, IAsyncDisposable
{
    private static readonly JsonSerializerOptions wire = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim oneTurnAtATime = new(1, 1);
    private readonly CodexOptions options;
    private readonly ILogger<CodexAppServer> logger;

    private Process? process;
    private int nextId;

    /// <summary>Creates the client.</summary>
    public CodexAppServer(CodexOptions options, ILogger<CodexAppServer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// One sequential read loop rather than a reader task beside the request calls. The
    /// protocol is strictly ordered — handshake, thread, turn, then deltas — and two
    /// things reading one stdio pipe race for lines, which loses the first fragments of
    /// the answer to whichever loop happened to win.
    /// </remarks>
    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        string workingDirectory,
        TimeSpan timeout,
        IReadOnlyList<string> imagePaths,
        FrontierToolbox tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Validate(prompt, workingDirectory, tools);
        await this.oneTurnAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        Process? live = null;
        var completed = false;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            live = this.Ensure(workingDirectory);
            await this.OpenTurnAsync(live, prompt, imagePaths, workingDirectory, tools, deadline.Token)
                .ConfigureAwait(false);

            await foreach (var fragment in this.ReadDeltasAsync(live, tools, deadline.Token)
                .ConfigureAwait(false))
            {
                yield return fragment;
            }

            completed = true;
        }
        finally
        {
            if (!completed && live is not null)
            {
                await this.StopAsync(live).ConfigureAwait(false);
            }

            this.oneTurnAtATime.Release();
        }
    }

    private static void Validate(string prompt, string workingDirectory, FrontierToolbox tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(tools);
    }

    /// <summary>Handshake, a fresh thread, then the turn — in protocol order.</summary>
    private async Task OpenTurnAsync(
        Process live, string prompt, IReadOnlyList<string> imagePaths, string workingDirectory,
        FrontierToolbox tools, CancellationToken cancellationToken)
    {
        await this.SendAsync(live, "initialize", InitializeParams(), cancellationToken).ConfigureAwait(false);

        await this.SendAsync(live, "thread/start", ThreadStartParams(workingDirectory, tools), cancellationToken)
            .ConfigureAwait(false);

        // The thread id lives at result.thread.id, not result.threadId — a detail that
        // silently produced an empty stream until it was traced. Reading skips the
        // initialize reply and any notification on the way.
        var threadId = await ReadResultAsync(live, "thread", "id", cancellationToken)
            .ConfigureAwait(false);

        await this.SendAsync(live, "turn/start", new
        {
            threadId,
            input = TurnInput(prompt, imagePaths),
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The handshake. <c>experimentalApi</c> is what unlocks <c>dynamicTools</c> on
    /// <c>thread/start</c>; without it the server answers "requires experimentalApi
    /// capability" and nothing else changes — found by probing, 2026-09-04.
    /// </summary>
    public static object InitializeParams() => new
    {
        clientInfo = new { name = "dami", title = "Dami", version = "1.0" },
        capabilities = new { experimentalApi = true },
    };

    /// <summary>A thread in the working directory, offering the bundle's tools if any.</summary>
    public static object ThreadStartParams(string workingDirectory, FrontierToolbox tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var parameters = new Dictionary<string, object> { ["cwd"] = workingDirectory };
        if (!tools.IsEmpty)
        {
            parameters["dynamicTools"] = tools.Tools.Select(tool => new
            {
                type = "function",
                name = tool.Name,
                description = tool.Description,
                inputSchema = tool.InputSchema,
            }).ToArray();
        }

        return parameters;
    }

    /// <summary>The installed app-server's shape for answering <c>item/tool/call</c>.</summary>
    public static object ToolCallResponse(FrontierToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new
        {
            success = result.Success,
            contentItems = new[] { new { type = "inputText", text = result.Text } },
        };
    }

    /// <summary>Builds the installed app-server's text plus local-image wire input.</summary>
    public static object[] TurnInput(string prompt, IReadOnlyList<string> imagePaths) =>
        [new { type = "text", text = prompt }, .. imagePaths.Select(
            path => (object)new { type = "localImage", path })];

    /// <summary>Reads until a response carrying the named nested field arrives.</summary>
    private static async Task<string> ReadResultAsync(
        Process live, string outer, string inner, CancellationToken cancellationToken)
    {
        while (await live.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            is { } line)
        {
            if (Read(line) is { } message
                && message.TryGetProperty("result", out var result)
                && result.TryGetProperty(outer, out var nested)
                && nested.TryGetProperty(inner, out var value)
                && value.GetString() is { Length: > 0 } found)
            {
                return found;
            }
        }

        throw new InvalidOperationException("codex app-server closed before answering");
    }

    /// <summary>
    /// Yields answer fragments until the turn ends, answering tool calls on the way. The
    /// turn may be silent only until its first token or tool call; after that the
    /// overall deadline is the only clock.
    /// </summary>
    private async IAsyncEnumerable<string> ReadDeltasAsync(
        Process live,
        FrontierToolbox tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        quiet.CancelAfter(TimeSpan.FromSeconds(this.options.FirstTokenTimeoutSeconds));
        while (await this.ReadLineAsync(live, quiet, cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (Read(line) is not { } message || !message.TryGetProperty("method", out var method))
            {
                continue;
            }

            var name = method.GetString();
            if (name == "item/tool/call")
            {
                quiet.CancelAfter(Timeout.InfiniteTimeSpan);
                await this.AnswerToolCallAsync(live, message, tools, cancellationToken).ConfigureAwait(false);
            }
            else if (name == "item/agentMessage/delta" && Delta(message) is { } fragment)
            {
                quiet.CancelAfter(Timeout.InfiniteTimeSpan);
                yield return fragment;
            }
            else if (name == "turn/failed")
            {
                throw new InvalidOperationException("the subscription turn failed");
            }
            else if (name == "turn/completed")
            {
                ThrowIfTurnFailed(message);
                yield break;
            }
        }
    }

    /// <summary>
    /// Runs the call and replies on the request's id. The turn is blocked until this
    /// answers, so a handler failure becomes a failed result, never a missing reply.
    /// </summary>
    private async Task AnswerToolCallAsync(
        Process live, JsonElement request, FrontierToolbox tools, CancellationToken cancellationToken)
    {
        var parameters = request.GetProperty("params");
        var call = new FrontierToolCall(
            parameters.GetProperty("callId").GetString() ?? string.Empty,
            parameters.GetProperty("tool").GetString() ?? string.Empty,
            parameters.GetProperty("arguments"));
        FrontierToolResult result;
        try
        {
            result = await tools.Handler.HandleAsync(call, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Frontier tool {Tool} failed", call.Tool);
            result = FrontierToolResult.Failed(exception.Message);
        }

        this.logger.LogInformation("Frontier tool {Tool} answered (success: {Success})", call.Tool, result.Success);
        var frame = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id = request.GetProperty("id"), result = ToolCallResponse(result) }, wire);
        await live.StandardInput.WriteLineAsync(frame.AsMemory(), cancellationToken).ConfigureAwait(false);
        await live.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One line, or a named failure when the turn stayed silent too long.</summary>
    private async Task<string?> ReadLineAsync(
        Process live, CancellationTokenSource quiet, CancellationToken outer)
    {
        try
        {
            return await live.StandardOutput.ReadLineAsync(quiet.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!outer.IsCancellationRequested)
        {
            this.logger.LogWarning(
                "codex app-server said nothing for {Seconds}s; giving up on the turn",
                this.options.FirstTokenTimeoutSeconds);
            throw new OperationCanceledException(
                $"the frontier produced nothing for {this.options.FirstTokenTimeoutSeconds}s");
        }
    }

    private static string? Delta(JsonElement message) =>
        message.TryGetProperty("params", out var parameters)
        && parameters.TryGetProperty("delta", out var delta)
        && delta.GetString() is { Length: > 0 } fragment
            ? fragment
            : null;

    private static void ThrowIfTurnFailed(JsonElement message)
    {
        if (!message.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("turn", out var turn)
            || !turn.TryGetProperty("error", out var error)
            || error.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var detail = error.TryGetProperty("message", out var value)
            ? value.GetString()
            : null;
        throw new InvalidOperationException(detail ?? "the subscription turn failed");
    }

    /// <summary>Starts the process if it is not already running, and hands back its pipes.</summary>
    private Process Ensure(string workingDirectory)
    {
        if (this.process is { HasExited: false })
        {
            return this.process;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = this.options.BinaryPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add("app-server");

        this.process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start {this.options.BinaryPath} app-server");
        this.logger.LogInformation("codex app-server started (pid {Pid})", this.process.Id);
        return this.process;
    }

    private async Task SendAsync(
        Process live, string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref this.nextId);
        var frame = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id, method, @params = parameters }, wire);
        await live.StandardInput.WriteLineAsync(frame.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await live.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }



    private static JsonElement? Read(string line)
    {
        try
        {
            return JsonDocument.Parse(line).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.process is { } live)
        {
            await this.StopAsync(live).ConfigureAwait(false);
        }

        this.oneTurnAtATime.Dispose();
    }

    private async Task StopAsync(Process live)
    {
        try
        {
            if (!live.HasExited)
            {
                live.Kill(entireProcessTree: true);
                await live.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // Already gone; nothing to clean up.
        }
        finally
        {
            if (ReferenceEquals(this.process, live))
            {
                this.process = null;
            }

            live.Dispose();
        }
    }
}
