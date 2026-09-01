using System.Globalization;
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

/// <summary>The OpenAI images API behind the same gate the frontier chat sits behind.</summary>
/// <remarks>
/// A near-copy of <see cref="AnthropicChatClient"/>'s enforcement, and deliberately so:
/// the boundary should look the same whichever door is used. Non-Egressable prompts are
/// refused even though callers should make that unreachable; the provider host is on the
/// ordinary allowlist; every call, allowed or refused, lands in the event stream carrying
/// its purpose and never its prompt.
///
/// Ported from what Hermes ran on the Mac — a Clawdbot skill shelling out to
/// <c>gen.py --model gpt-image-1</c> with the key on the command line and no record that
/// the call happened. Same provider, same model; the difference is that this one is
/// refusable, budgeted and auditable.
/// </remarks>
public sealed class OpenAiImageGenerator : IImageGenerator
{
    private const string ACTOR = "image-openai";

    private readonly HttpClient httpClient;
    private readonly OpenAiImageOptions imageOptions;
    private readonly EgressOptions egressOptions;
    private readonly IEgressBudget egressBudget;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<OpenAiImageGenerator> logger;

    /// <summary>Creates the generator.</summary>
    public OpenAiImageGenerator(
        HttpClient httpClient,
        IOptions<OpenAiImageOptions> imageOptions,
        IOptions<EgressOptions> egressOptions,
        IEgressBudget egressBudget,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<OpenAiImageGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(imageOptions);
        ArgumentNullException.ThrowIfNull(egressOptions);
        ArgumentNullException.ThrowIfNull(egressBudget);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.imageOptions = imageOptions.Value;
        this.egressOptions = egressOptions.Value;
        this.egressBudget = egressBudget;
        this.eventStore = eventStore;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<GeneratedImage> GenerateAsync(
        ImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var host = new Uri(this.imageOptions.BaseUrl).Host;
        await this.EmitAsync(
            request, ExecutionEventType.EgressRequested, ExecutionStatus.Running,
            $"{request.Purpose} -> {host}", cancellationToken).ConfigureAwait(false);

        var refusal = this.FindRefusal(host, request)
            ?? await this.egressBudget.FindRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            await this.EmitAsync(
                request, ExecutionEventType.EgressRefused, ExecutionStatus.Failed, refusal,
                cancellationToken).ConfigureAwait(false);
            this.logger.LogWarning("Image generation refused: {Reason}", refusal);
            throw new EgressRefusedException(refusal);
        }

        var image = await this.SendOrRecordFailureAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await this.EmitAsync(
            request, ExecutionEventType.EgressCompleted, ExecutionStatus.Succeeded,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{request.Purpose}: {image.Bytes.Length} bytes returned"),
            cancellationToken).ConfigureAwait(false);

        return image;
    }

    private string? FindRefusal(string host, ImageRequest request)
    {
        if (request.Privacy != PrivacyClass.Egressable)
        {
            return "the prompt is not Egressable; local-only content never reaches an image provider (D-012)";
        }

        var allowed = this.egressOptions.AllowedHosts
            .Any(candidate => string.Equals(candidate, host, StringComparison.OrdinalIgnoreCase));
        if (!allowed)
        {
            return $"provider host '{host}' is not on the egress allowlist; being configured does not exempt it";
        }

        if (string.IsNullOrEmpty(this.imageOptions.ApiKey))
        {
            return "no API key is configured; image generation is absent, not assumed";
        }

        return null;
    }

    /// <summary>Sends, recording a failure rather than leaving a dangling request.</summary>
    /// <remarks>
    /// By the time this throws the prompt is already on the wire. An `EgressRequested`
    /// with no outcome is indistinguishable from a call that never happened, which is the
    /// worst possible state for the one ledger that is supposed to say what left.
    /// </remarks>
    private async Task<GeneratedImage> SendOrRecordFailureAsync(
        ImageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await this.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await this.EmitAsync(
                request, ExecutionEventType.EgressFailed, ExecutionStatus.Failed,
                $"{request.Purpose}: {exception.GetType().Name}", cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<GeneratedImage> SendAsync(
        ImageRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(this.imageOptions.BaseUrl), "/v1/images/generations"));
        message.Headers.Add("Authorization", "Bearer " + this.imageOptions.ApiKey);
        message.Content = JsonContent.Create(new
        {
            model = this.imageOptions.Model,
            prompt = request.Prompt,
            size = request.Size,
            quality = request.Quality,
            n = 1,
        });

        using var response = await this.httpClient
            .SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Read(body, request);
    }

    /// <summary>Reads the response. gpt-image-1 always returns base64, never a URL.</summary>
    /// <remarks>Public so the wire format can be tested without a provider.</remarks>
    public static GeneratedImage Read(string body, ImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(request);

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0
            || !data[0].TryGetProperty("b64_json", out var encoded)
            || encoded.GetString() is not { Length: > 0 } payload)
        {
            throw new InvalidOperationException("The image provider returned no image.");
        }

        return new GeneratedImage(
            $"{request.TraceId:N}.png",
            Convert.FromBase64String(payload),
            "image/png",
            request.Prompt);
    }

    private Task<long> EmitAsync(
        ImageRequest request,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        return this.eventStore.AppendAsync(
            new ExecutionEvent(
                eventId: Guid.NewGuid(),
                traceId: request.TraceId,
                spanId: Guid.NewGuid(),
                parentSpanId: null,
                origin: request.Origin,
                actorId: ACTOR,
                type: type,
                status: status,
                occurredAt: this.clock.GetUtcNow(),
                label: label),
            cancellationToken);
    }
}
