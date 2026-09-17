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

    /// <summary>
    /// What a chat thread is, said to the model. The sandbox and the approval policy make
    /// its own tools fail; this stops it spending a minute discovering that, one failed
    /// connector at a time, before answering.
    /// </summary>
    public const string CHAT_THREAD_INSTRUCTIONS =
        "This is a chat turn inside Dami's runtime, not a coding session. You have no browser, "
        + "no shell, no file access, and no connectors here, and attempts to use them fail. The "
        + "only tools that work are the ones declared on this thread; use those, or answer from "
        + "what you were given. Never claim to have searched, browsed, or run anything you did not.";

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
            using var budget = new TurnBudget(deadline, timeout);
            live = this.Ensure(workingDirectory);
            await this.OpenTurnAsync(live, prompt, imagePaths, workingDirectory, tools, deadline.Token)
                .ConfigureAwait(false);

            await foreach (var fragment in this.ReadDeltasAsync(live, tools, budget)
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

        await this.SendAsync(live, "thread/start", ThreadStartParams(workingDirectory, tools, this.options), cancellationToken)
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
    /// <remarks>
    /// Ephemeral, read-only, never asking for approval, and without Codex's own web
    /// search: a chat turn that could write files, wait on an approval nobody answers
    /// (the ten-minute silences), or browse around the gated door is not a chat turn.
    /// </remarks>
    public static object ThreadStartParams(string workingDirectory, FrontierToolbox tools) =>
        ThreadStartParams(workingDirectory, tools, new CodexOptions());

    /// <summary>A thread in the working directory, offering the bundle's tools if any.</summary>
    public static object ThreadStartParams(string workingDirectory, FrontierToolbox tools, CodexOptions options)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(options);
        var parameters = new Dictionary<string, object>
        {
            ["cwd"] = workingDirectory,
            ["ephemeral"] = true,
            ["sandbox"] = options.Sandbox,
            ["approvalPolicy"] = "never",
            ["config"] = new Dictionary<string, object> { ["web_search"] = options.BuiltInWebSearch ? "live" : "disabled" },
            ["developerInstructions"] = CHAT_THREAD_INSTRUCTIONS,
        };
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
        TurnBudget budget,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
        quiet.CancelAfter(TimeSpan.FromSeconds(this.options.FirstTokenTimeoutSeconds));
        while (await this.ReadLineAsync(live, quiet, budget.Token).ConfigureAwait(false) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Read(line) is not { } message || !message.TryGetProperty("method", out var method))
            {
                continue;
            }

            var (fragment, done) = await this.HandleAsync(live, message, method.GetString(), tools, quiet, budget)
                .ConfigureAwait(false);
            if (fragment is not null)
            {
                yield return fragment;
            }

            if (done)
            {
                yield break;
            }
        }
    }

    /// <summary>One protocol message: a fragment to yield, the end of the turn, or nothing.</summary>
    private async Task<(string? Fragment, bool Done)> HandleAsync(
        Process live, JsonElement message, string? name, FrontierToolbox tools, CancellationTokenSource quiet,
        TurnBudget budget)
    {
        switch (name)
        {
            case "item/tool/call":
                quiet.CancelAfter(Timeout.InfiniteTimeSpan);
                await this.AnswerToolCallAsync(live, message, tools, budget).ConfigureAwait(false);
                return (null, false);
            case "item/agentMessage/delta":
                var fragment = Delta(message);
                if (fragment is not null)
                {
                    quiet.CancelAfter(Timeout.InfiniteTimeSpan);
                }

                return (fragment, false);
            case "turn/failed":
                throw new InvalidOperationException("the subscription turn failed");
            case "turn/completed":
                ThrowIfTurnFailed(message);
                return (null, true);
            case "item/started":
                this.NoteItem(message);
                return (null, false);
            default:
                if (message.TryGetProperty("id", out _))
                {
                    await this.DeclineAsync(live, message, name, budget.Token).ConfigureAwait(false);
                }

                return (null, false);
        }
    }

    /// <summary>
    /// Runs the call and replies on the request's id. The turn is blocked until this
    /// answers, so a handler failure becomes a failed result, never a missing reply.
    /// </summary>
    /// <summary>
    /// Runs a tool the model asked for and answers on its id. The turn's clock stops
    /// while the tool runs: on 2026-09-16 three portraits at ~3 min each spent the whole
    /// 600 s budget and the fourth was cancelled together with the answer. The budget
    /// bounds the model's time; each tool carries its own ceiling.
    /// </summary>
    private async Task AnswerToolCallAsync(
        Process live, JsonElement request, FrontierToolbox tools, TurnBudget budget)
    {
        var cancellationToken = budget.Token;
        var parameters = request.GetProperty("params");
        var call = new FrontierToolCall(
            parameters.GetProperty("callId").GetString() ?? string.Empty,
            parameters.GetProperty("tool").GetString() ?? string.Empty,
            parameters.GetProperty("arguments"));
        FrontierToolResult result;
        budget.Pause();
        try
        {
            result = await tools.Handler.HandleAsync(call, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Frontier tool {Tool} failed", call.Tool);
            result = FrontierToolResult.Failed(exception.Message);
        }
        finally
        {
            budget.Resume();
        }

        this.logger.LogInformation("Frontier tool {Tool} answered (success: {Success})", call.Tool, result.Success);
        var frame = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id = request.GetProperty("id"), result = ToolCallResponse(result) }, wire);
        await live.StandardInput.WriteLineAsync(frame.AsMemory(), cancellationToken).ConfigureAwait(false);
        await live.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers a server request we do not serve with a JSON-RPC error, so nothing waits.</summary>
    private async Task DeclineAsync(Process live, JsonElement request, string? method, CancellationToken cancellationToken)
    {
        this.logger.LogWarning("codex app-server asked {Method}; declined — chat threads never approve or answer prompts", method);
        var frame = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id = request.GetProperty("id"), error = new { code = -32601, message = $"dami does not serve {method}" } }, wire);
        await live.StandardInput.WriteLineAsync(frame.AsMemory(), cancellationToken).ConfigureAwait(false);
        await live.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Says what Codex started on its own — a command, a search — so its tool use is visible in the journal.</summary>
    private void NoteItem(JsonElement message)
    {
        if (message.TryGetProperty("params", out var parameters)
            && parameters.TryGetProperty("item", out var item)
            && item.TryGetProperty("type", out var type)
            && type.GetString() is { } kind && kind != "agentMessage" && kind != "dynamicToolCall")
        {
            this.logger.LogInformation("codex item started: {Kind}", kind);
        }
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

    /// <summary>
    /// The turn's wall-clock budget, spent only while the model is on the clock. A
    /// pause stops the clock for a tool; a resume re-arms the deadline with what is left.
    /// </summary>
    private sealed class TurnBudget : IDisposable
    {
        private readonly CancellationTokenSource source;
        private readonly TimeSpan total;
        private readonly Stopwatch onTheClock = new();

        public TurnBudget(CancellationTokenSource source, TimeSpan total)
        {
            this.source = source;
            this.total = total;
            this.Resume();
        }

        public CancellationToken Token => this.source.Token;

        public void Pause()
        {
            this.onTheClock.Stop();
            this.source.CancelAfter(Timeout.InfiniteTimeSpan);
        }

        public void Resume()
        {
            var remaining = this.total - this.onTheClock.Elapsed;
            this.onTheClock.Start();
            this.source.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }

        public void Dispose() => this.onTheClock.Stop();
    }
}
