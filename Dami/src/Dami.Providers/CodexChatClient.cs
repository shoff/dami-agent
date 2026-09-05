using System.Runtime.CompilerServices;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>The subscription frontier: Steve's ChatGPT login through the Codex CLI (ADR-0011).</summary>
/// <remarks>
/// The ADR-0010 gate, mapped to a subprocess: a non-Egressable prompt is refused before
/// anything spawns; the capability exists only while <see cref="CodexOptions.Enabled"/>
/// is deliberately true; every call lands in the caller's trace with the purpose line
/// and never the prompt. The process itself runs read-only, in a scratch directory,
/// outside any repository — the frontier model gets the prompt and nothing else. The
/// adapter never touches credentials; the CLI owns its own browser login.
/// </remarks>
public sealed class CodexChatClient : IFrontierChat
{
    private const string ACTOR = "frontier-codex";

    private readonly ICodexProcess codexProcess;
    private readonly ICodexAppServer appServer;
    private readonly CodexOptions codexOptions;
    private readonly IExecutionEventStore eventStore;
    private readonly IEgressBudget egressBudget;
    private readonly TimeProvider clock;
    private readonly ILogger<CodexChatClient> logger;

    /// <summary>Creates the client.</summary>
    public CodexChatClient(
        ICodexProcess codexProcess,
        ICodexAppServer appServer,
        IOptions<CodexOptions> codexOptions,
        IExecutionEventStore eventStore,
        IEgressBudget egressBudget,
        TimeProvider clock,
        ILogger<CodexChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(codexProcess);
        ArgumentNullException.ThrowIfNull(appServer);
        ArgumentNullException.ThrowIfNull(codexOptions);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(egressBudget);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.codexProcess = codexProcess;
        this.appServer = appServer;
        this.codexOptions = codexOptions.Value;
        this.eventStore = eventStore;
        this.egressBudget = egressBudget;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(FrontierPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        await this.EmitAsync(
            prompt, ExecutionEventType.EgressRequested, ExecutionStatus.Running,
            $"{prompt.Purpose} -> codex subscription", cancellationToken).ConfigureAwait(false);

        var refusal = this.FindRefusal(prompt)
            ?? await this.egressBudget.FindRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            await this.EmitAsync(
                prompt, ExecutionEventType.EgressRefused, ExecutionStatus.Failed, refusal, cancellationToken)
                .ConfigureAwait(false);
            this.logger.LogWarning("Subscription frontier refused: {Reason}", refusal);
            throw new EgressRefusedException(refusal);
        }

        var answer = await this.codexProcess.RunAsync(
            this.codexOptions.BinaryPath,
            this.BuildArguments(prompt.Prompt),
            TimeSpan.FromSeconds(this.codexOptions.TimeoutSeconds),
            cancellationToken).ConfigureAwait(false);

        await this.EmitAsync(
            prompt, ExecutionEventType.EgressCompleted, ExecutionStatus.Succeeded,
            $"{prompt.Purpose}: {answer.Length} chars returned", cancellationToken).ConfigureAwait(false);

        return answer;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Streams over <c>codex app-server</c> rather than <c>codex exec</c>. Same
    /// subscription, same account, no API key — `exec` writes its answer to a file that
    /// is readable only once the process has exited, which is why the batch path cannot
    /// stream and this one can.
    /// </remarks>
    public async IAsyncEnumerable<string> StreamAsync(
        FrontierPrompt prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        await this.EmitAsync(
            prompt, ExecutionEventType.EgressRequested, ExecutionStatus.Running,
            $"{prompt.Purpose} -> codex subscription (streaming)", cancellationToken)
            .ConfigureAwait(false);

        await this.RefuseIfNeededAsync(prompt, cancellationToken).ConfigureAwait(false);

        var characters = 0;
        await foreach (var fragment in this.appServer.StreamAsync(
                prompt.Prompt,
                this.codexOptions.WorkingDirectory,
                TimeSpan.FromSeconds(this.codexOptions.TimeoutSeconds),
                [],
                FrontierToolbox.Empty,
                cancellationToken).ConfigureAwait(false))
        {
            characters += fragment.Length;
            yield return fragment;
        }

        await this.EmitAsync(
            prompt, ExecutionEventType.EgressCompleted, ExecutionStatus.Succeeded,
            $"{prompt.Purpose}: {characters} chars streamed", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        FrontierPrompt prompt,
        IReadOnlyList<FrontierImage> images,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (images.Count == 0)
        {
            await foreach (var fragment in this.StreamAsync(prompt, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return fragment;
            }

            yield break;
        }

        var paths = await WriteImagesAsync(images, cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var fragment in this.StreamImagesAsync(prompt, paths, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return fragment;
            }
        }
        finally
        {
            foreach (var path in paths)
            {
                File.Delete(path);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The bundle rides the app-server's dynamic tools: declared on the thread, called
    /// back mid-turn, answered on this host. The subscription door is the only frontier
    /// that hosts tools, which is the point of routing Discord through it.
    /// </remarks>
    public async IAsyncEnumerable<string> StreamAsync(
        FrontierPrompt prompt,
        IReadOnlyList<FrontierImage> images,
        FrontierToolbox tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(tools);

        if (tools.IsEmpty)
        {
            await foreach (var fragment in this.StreamAsync(prompt, images, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return fragment;
            }

            yield break;
        }

        await foreach (var fragment in this.StreamToolTurnAsync(prompt, images, tools, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return fragment;
        }
    }

    private async IAsyncEnumerable<string> StreamToolTurnAsync(
        FrontierPrompt prompt,
        IReadOnlyList<FrontierImage> images,
        FrontierToolbox tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await this.EmitAsync(
            prompt, ExecutionEventType.EgressRequested, ExecutionStatus.Running,
            $"{prompt.Purpose} -> codex subscription (streaming, {tools.Tools.Count} tool(s))",
            cancellationToken).ConfigureAwait(false);
        var paths = await WriteImagesAsync(images, cancellationToken).ConfigureAwait(false);
        var characters = 0;
        try
        {
            await foreach (var fragment in this.StreamImagesAsync(prompt, paths, tools, cancellationToken)
                .ConfigureAwait(false))
            {
                characters += fragment.Length;
                yield return fragment;
            }
        }
        finally
        {
            foreach (var path in paths)
            {
                File.Delete(path);
            }
        }

        await this.EmitAsync(
            prompt, ExecutionEventType.EgressCompleted, ExecutionStatus.Succeeded,
            $"{prompt.Purpose}: {characters} chars streamed", cancellationToken).ConfigureAwait(false);
    }

    private IAsyncEnumerable<string> StreamImagesAsync(
        FrontierPrompt prompt, IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        this.StreamImagesAsync(prompt, paths, FrontierToolbox.Empty, cancellationToken);

    private async IAsyncEnumerable<string> StreamImagesAsync(
        FrontierPrompt prompt,
        IReadOnlyList<string> paths,
        FrontierToolbox tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await this.RefuseIfNeededAsync(prompt, cancellationToken).ConfigureAwait(false);
        await foreach (var fragment in this.appServer.StreamAsync(
            prompt.Prompt, this.codexOptions.WorkingDirectory,
            TimeSpan.FromSeconds(this.codexOptions.TimeoutSeconds), paths, tools, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return fragment;
        }
    }

    private static async Task<IReadOnlyList<string>> WriteImagesAsync(
        IReadOnlyList<FrontierImage> images, CancellationToken cancellationToken)
    {
        var paths = new List<string>(images.Count);
        foreach (var image in images)
        {
            var extension = Path.GetExtension(image.FileName);
            var path = Path.Combine(Path.GetTempPath(), $"dami-chat-{Guid.NewGuid():N}{extension}");
            await File.WriteAllBytesAsync(path, image.Bytes.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            paths.Add(path);
        }

        return paths;
    }

    /// <summary>Throws if this prompt or this moment may not reach the frontier.</summary>
    private async Task RefuseIfNeededAsync(
        FrontierPrompt prompt, CancellationToken cancellationToken)
    {
        var refusal = this.FindRefusal(prompt)
            ?? await this.egressBudget.FindRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is null)
        {
            return;
        }

        await this.EmitAsync(
            prompt, ExecutionEventType.EgressRefused, ExecutionStatus.Failed, refusal,
            cancellationToken).ConfigureAwait(false);
        this.logger.LogWarning("Subscription frontier refused: {Reason}", refusal);
        throw new EgressRefusedException(refusal);
    }

    private string? FindRefusal(FrontierPrompt prompt)
    {
        if (prompt.Privacy != PrivacyClass.Egressable)
        {
            return "the prompt is not Egressable; local-only content never reaches a frontier provider (D-012)";
        }

        if (!this.codexOptions.Enabled)
        {
            return "the subscription frontier is not enabled; frontier capability is a deliberate act (ADR-0011)";
        }

        return null;
    }

    private List<string> BuildArguments(string prompt)
    {
        var arguments = new List<string>
        {
            "exec",
            "--sandbox", "read-only",
            "--skip-git-repo-check",
            "--cd", this.codexOptions.WorkingDirectory,
        };

        if (!string.IsNullOrEmpty(this.codexOptions.Model))
        {
            arguments.Add("--model");
            arguments.Add(this.codexOptions.Model);
        }

        arguments.Add(prompt);
        return arguments;
    }

    private Task<long> EmitAsync(
        FrontierPrompt prompt,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        var executionEvent = new ExecutionEvent(
            eventId: Guid.NewGuid(),
            traceId: prompt.TraceId,
            spanId: Guid.NewGuid(),
            parentSpanId: null,
            origin: prompt.Origin,
            actorId: ACTOR,
            type: type,
            status: status,
            occurredAt: this.clock.GetUtcNow(),
            label: label);

        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
