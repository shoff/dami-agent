using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>The Anthropic Messages API behind the ADR-0010 gate.</summary>
/// <remarks>
/// Enforcement lives here, not in callers: a non-Egressable prompt is refused even
/// though the router makes that unreachable; the provider host must be on the same
/// egress allowlist as every other destination; and every call — allowed or refused —
/// lands in the event stream with the purpose line, never the prompt text.
/// </remarks>
public sealed class AnthropicChatClient : IFrontierChat
{
    private const string ACTOR = "frontier-anthropic";

    private readonly HttpClient httpClient;
    private readonly AnthropicOptions anthropicOptions;
    private readonly EgressOptions egressOptions;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<AnthropicChatClient> logger;

    /// <summary>Creates the client.</summary>
    public AnthropicChatClient(
        HttpClient httpClient,
        IOptions<AnthropicOptions> anthropicOptions,
        IOptions<EgressOptions> egressOptions,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<AnthropicChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(anthropicOptions);
        ArgumentNullException.ThrowIfNull(egressOptions);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.anthropicOptions = anthropicOptions.Value;
        this.egressOptions = egressOptions.Value;
        this.eventStore = eventStore;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(FrontierPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        await this.EmitAsync(
            prompt, ExecutionEventType.EgressRequested, ExecutionStatus.Running,
            $"{prompt.Purpose} -> {new Uri(this.anthropicOptions.BaseUrl).Host}", cancellationToken)
            .ConfigureAwait(false);

        var refusal = this.FindRefusal(prompt);
        if (refusal is not null)
        {
            await this.EmitAsync(
                prompt, ExecutionEventType.EgressRefused, ExecutionStatus.Failed, refusal, cancellationToken)
                .ConfigureAwait(false);
            this.logger.LogWarning("Frontier call refused: {Reason}", refusal);
            throw new EgressRefusedException(refusal);
        }

        var answer = await this.SendOrRecordFailureAsync(prompt, cancellationToken)
            .ConfigureAwait(false);

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

        var host = new Uri(this.anthropicOptions.BaseUrl).Host;
        var allowed = this.egressOptions.AllowedHosts
            .Any(candidate => string.Equals(candidate, host, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            return $"provider host '{host}' is not on the egress allowlist; being configured does not exempt it";
        }

        if (string.IsNullOrEmpty(this.anthropicOptions.ApiKey))
        {
            return "no API key is configured; frontier capability is absent, not assumed";
        }

        return null;
    }

    /// <summary>Sends, recording a failure rather than leaving a dangling request.</summary>
    /// <remarks>
    /// By the time this throws the prompt is already on the wire. An `EgressRequested`
    /// with no outcome is indistinguishable from a call that never happened.
    /// </remarks>
    private async Task<string> SendOrRecordFailureAsync(
        FrontierPrompt prompt, CancellationToken cancellationToken)
    {
        try
        {
            return await this.SendAsync(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await this.EmitAsync(
                prompt, ExecutionEventType.EgressFailed, ExecutionStatus.Failed,
                $"{prompt.Purpose}: {exception.GetType().Name}", cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<string> SendAsync(FrontierPrompt prompt, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(this.anthropicOptions.BaseUrl), "/v1/messages"));
        request.Headers.Add("x-api-key", this.anthropicOptions.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model = this.anthropicOptions.Model,
            max_tokens = this.anthropicOptions.MaxTokens,
            messages = new[] { new { role = "user", content = prompt.Prompt } },
        });

        using var response = await this.httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var text = new System.Text.StringBuilder();
        foreach (var block in body.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
            {
                text.Append(block.GetProperty("text").GetString());
            }
        }

        return text.ToString();
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
