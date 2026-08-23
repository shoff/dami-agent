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
    private readonly CodexOptions codexOptions;
    private readonly IExecutionEventStore eventStore;
    private readonly IEgressBudget egressBudget;
    private readonly TimeProvider clock;
    private readonly ILogger<CodexChatClient> logger;

    /// <summary>Creates the client.</summary>
    public CodexChatClient(
        ICodexProcess codexProcess,
        IOptions<CodexOptions> codexOptions,
        IExecutionEventStore eventStore,
        IEgressBudget egressBudget,
        TimeProvider clock,
        ILogger<CodexChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(codexProcess);
        ArgumentNullException.ThrowIfNull(codexOptions);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(egressBudget);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.codexProcess = codexProcess;
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
