using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>The Gemini image API behind the same gate every other door sits behind.</summary>
/// <remarks>
/// Shaped like <see cref="OpenAiImageGenerator"/> on purpose: non-Egressable prompts are
/// refused, the provider host is on the ordinary allowlist, an absent key is absent
/// capability, the C5 budget applies, and every call lands in the event stream with its
/// purpose and never its prompt.
///
/// The wire is <c>models/{model}:generateContent</c> asked for an image-only response.
/// Gemini has no width-by-height vocabulary, so the request's size becomes an aspect ratio
/// and the pixel count comes from <see cref="GeminiImageOptions.ImageSize"/>. A reference
/// image travels as an inline part with a sentence saying whether it is the picture to
/// change or the identity to keep — the same rule the subscription door states in prose.
/// </remarks>
public sealed class GeminiImageGenerator : IImageGenerator
{
    private const string ACTOR = "image-gemini";

    private const string EDIT_RULE =
        "The attached image is the source picture. Apply exactly the change described below "
        + "and keep everything else — the subject and their identity, the composition, lighting "
        + "and setting — as it is.";

    private const string ANCHOR_RULE =
        "The attached image is the sole identity reference. Preserve that identity while "
        + "creating the new scene described below rather than editing the reference pose.";

    /// <summary>Gemini's accepted aspect ratios, as width over height.</summary>
    private static readonly (string Name, double Value)[] aspectRatios =
    [
        ("1:1", 1d), ("3:2", 1.5d), ("2:3", 2d / 3d), ("3:4", 0.75d), ("4:3", 4d / 3d),
        ("4:5", 0.8d), ("5:4", 1.25d), ("9:16", 9d / 16d), ("16:9", 16d / 9d), ("21:9", 21d / 9d),
    ];

    private static readonly JsonSerializerOptions wire = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient httpClient;
    private readonly GeminiImageOptions imageOptions;
    private readonly EgressOptions egressOptions;
    private readonly IEgressBudget egressBudget;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<GeminiImageGenerator> logger;

    /// <summary>Creates the generator.</summary>
    public GeminiImageGenerator(
        HttpClient httpClient,
        IOptions<GeminiImageOptions> imageOptions,
        IOptions<EgressOptions> egressOptions,
        IEgressBudget egressBudget,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<GeminiImageGenerator> logger)
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
            this.logger.LogWarning("Gemini image generation refused: {Reason}", refusal);
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
        using var message = this.CreateMessage(request);
        // The header form; the query-string form Google also accepts would put the key in
        // every proxy and access log between here and them.
        message.Headers.Add("x-goog-api-key", this.imageOptions.ApiKey);
        using var response = await this.httpClient
            .SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Google's body says why — "free tier, limit: 0" on 2026-09-16 — where the
            // status code alone says only that it did not draw.
            throw new HttpRequestException(
                $"Gemini answered {(int)response.StatusCode} {response.StatusCode}: {ErrorMessage(body)}",
                null, response.StatusCode);
        }

        return Read(body, request);
    }

    /// <summary>The provider's own explanation from an error body, bounded, or the raw body.</summary>
    private static string ErrorMessage(string body)
    {
        string text;
        try
        {
            using var document = JsonDocument.Parse(body);
            text = document.RootElement.TryGetProperty("error", out var error)
                ? (error.TryGetProperty("status", out var status) ? status.GetString() : null)
                    + ": " + (error.TryGetProperty("message", out var message) ? message.GetString() : null)
                : body;
        }
        catch (JsonException)
        {
            text = body;
        }

        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 400 ? flat : flat[..400] + "…";
    }

    private HttpRequestMessage CreateMessage(ImageRequest request)
    {
        var route = $"/v1beta/models/{this.imageOptions.Model}:generateContent";
        return new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(this.imageOptions.BaseUrl), route))
        {
            Content = JsonContent.Create(this.CreateBody(request), options: wire),
        };
    }

    private object CreateBody(ImageRequest request) => new
    {
        contents = new[] { new { role = "user", parts = Parts(request) } },
        generationConfig = new
        {
            responseModalities = new[] { "IMAGE" },
            imageConfig = new
            {
                aspectRatio = AspectRatio(request.Size),
                imageSize = this.imageOptions.ImageSize.Length > 0 ? this.imageOptions.ImageSize : null,
            },
        },
    };

    /// <summary>The text part, preceded by the reference and its rule when there is one.</summary>
    private static List<object> Parts(ImageRequest request)
    {
        var parts = new List<object>(2);
        if (request.Reference is null)
        {
            parts.Add(new { text = request.Prompt });
            return parts;
        }

        parts.Add(new
        {
            inlineData = new
            {
                mimeType = request.Reference.ContentType,
                data = Convert.ToBase64String(request.Reference.Bytes.Span),
            },
        });
        var rule = request.EditReference ? EDIT_RULE : ANCHOR_RULE;
        parts.Add(new { text = rule + "\n\n" + request.Prompt });
        return parts;
    }

    /// <summary>The closest ratio Gemini accepts for a "WxH" size, or null to let the model pick.</summary>
    /// <remarks>Public so the translation can be tested without a provider.</remarks>
    public static string? AspectRatio(string size)
    {
        ArgumentNullException.ThrowIfNull(size);

        var separator = size.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (separator <= 0
            || !int.TryParse(size.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(size.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            || width <= 0 || height <= 0)
        {
            return null;
        }

        var wanted = (double)width / height;
        string? best = null;
        var bestDistance = double.MaxValue;
        foreach (var (name, value) in aspectRatios)
        {
            var distance = Math.Abs(value - wanted) / wanted;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = name;
            }
        }

        return bestDistance <= 0.05d ? best : null;
    }

    /// <summary>Reads the response: the first inline image part of the first candidate.</summary>
    /// <remarks>Public so the wire format can be tested without a provider.</remarks>
    public static GeneratedImage Read(string body, ImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(request);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "The image provider returned no candidate" + Reason(root, "promptFeedback", "blockReason") + ".");
        }

        var candidate = candidates[0];
        if (candidate.TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array
            && FindImage(parts, out var mimeType, out var payload))
        {
            return new GeneratedImage(
                $"{request.TraceId:N}{Extension(mimeType)}",
                Convert.FromBase64String(payload),
                mimeType,
                request.Prompt);
        }

        throw new InvalidOperationException(
            "The image provider returned no image" + Reason(candidate, "finishReason") + ".");
    }

    private static bool FindImage(JsonElement parts, out string mimeType, out string payload)
    {
        foreach (var part in parts.EnumerateArray())
        {
            if ((part.TryGetProperty("inlineData", out var inline) || part.TryGetProperty("inline_data", out inline))
                && inline.TryGetProperty("data", out var data)
                && data.GetString() is { Length: > 0 } encoded)
            {
                var declared = inline.TryGetProperty("mimeType", out var mime) || inline.TryGetProperty("mime_type", out mime)
                    ? mime.GetString()
                    : null;
                mimeType = declared is { Length: > 0 } ? declared : "image/png";
                payload = encoded;
                return true;
            }
        }

        mimeType = string.Empty;
        payload = string.Empty;
        return false;
    }

    /// <summary>" (reason: X)" when the element carries the named field, else nothing.</summary>
    private static string Reason(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return string.Empty;
            }
        }

        return current.GetString() is { Length: > 0 } reason ? $" (reason: {reason})" : string.Empty;
    }

    private static string Extension(string mimeType) => mimeType switch
    {
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => ".png",
    };

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
