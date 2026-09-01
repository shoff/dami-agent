using System.Diagnostics;
using System.Text.Json;
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
        string prompt, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken);
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        await this.oneTurnAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            var live = this.Ensure(workingDirectory);
            await this.OpenTurnAsync(live, prompt, workingDirectory, deadline.Token)
                .ConfigureAwait(false);

            await foreach (var fragment in ReadDeltasAsync(live, deadline.Token).ConfigureAwait(false))
            {
                yield return fragment;
            }
        }
        finally
        {
            this.oneTurnAtATime.Release();
        }
    }

    /// <summary>Handshake, a fresh thread, then the turn — in protocol order.</summary>
    private async Task OpenTurnAsync(
        Process live, string prompt, string workingDirectory, CancellationToken cancellationToken)
    {
        await this.SendAsync(live, "initialize", new
        {
            clientInfo = new { name = "dami", title = "Dami", version = "1.0" },
        }, cancellationToken).ConfigureAwait(false);

        await this.SendAsync(live, "thread/start", new { cwd = workingDirectory }, cancellationToken)
            .ConfigureAwait(false);

        // The thread id lives at result.thread.id, not result.threadId — a detail that
        // silently produced an empty stream until it was traced. Reading skips the
        // initialize reply and any notification on the way.
        var threadId = await ReadResultAsync(live, "thread", "id", cancellationToken)
            .ConfigureAwait(false);

        await this.SendAsync(live, "turn/start", new
        {
            threadId,
            input = new[] { new { type = "text", text = prompt } },
        }, cancellationToken).ConfigureAwait(false);
    }

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

    /// <summary>Yields answer fragments until the turn ends.</summary>
    private static async IAsyncEnumerable<string> ReadDeltasAsync(
        Process live,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await live.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            is { } line)
        {
            if (Read(line) is not { } message
                || !message.TryGetProperty("method", out var method))
            {
                continue;
            }

            var name = method.GetString();
            if (name == "item/agentMessage/delta"
                && message.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("delta", out var delta)
                && delta.GetString() is { Length: > 0 } fragment)
            {
                yield return fragment;
            }
            else if (name is "turn/completed" or "turn/failed")
            {
                yield break;
            }
        }
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
        if (this.process is { HasExited: false } live)
        {
            try
            {
                live.Kill(entireProcessTree: true);
                await live.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
            {
                // Already gone; nothing to clean up.
            }
        }

        this.process?.Dispose();
        this.oneTurnAtATime.Dispose();
    }
}
