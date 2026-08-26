using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Workers;

namespace Dami.Host;

/// <summary>Text to speech (L4), run as a bounded worker under a real trace.</summary>
public static class SpeechEndpoints
{
    private const int MAX_CHARS = 4000;
    private static readonly TimeSpan bound = TimeSpan.FromMinutes(2);

    /// <summary>Maps the speech route.</summary>
    public static void Map(WebApplication app)
    {
        app.MapPost("/speak", SpeakAsync);
    }

    private static async Task<IResult> SpeakAsync(
        SpeakRequest request,
        ISpeechClient speech,
        IWorkerRunner workers,
        IExecutionEventStore events,
        TimeProvider clock,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MAX_CHARS)
        {
            return Results.BadRequest(new { error = $"text is required and at most {MAX_CHARS} characters" });
        }

        var traceId = Guid.NewGuid();
        var rootSpanId = Guid.NewGuid();
        await MarkAsync(events, traceId, rootSpanId, clock, ExecutionEventType.TraceStarted,
            ExecutionStatus.Running, $"speak {request.Text.Length} chars", token).ConfigureAwait(false);

        var result = await workers.RunAsync(
            "text-to-speech", traceId, rootSpanId, bound,
            async inner => Convert.ToBase64String(await speech.SpeakAsync(request.Text, inner).ConfigureAwait(false)),
            token).ConfigureAwait(false);

        await MarkAsync(events, traceId, rootSpanId, clock,
            result.Succeeded ? ExecutionEventType.TraceCompleted : ExecutionEventType.TraceFailed,
            result.Succeeded ? ExecutionStatus.Succeeded : ExecutionStatus.Failed,
            $"speak {(result.Succeeded ? "done" : "failed")}", token).ConfigureAwait(false);

        return Results.Ok(new { traceId, audioBase64 = result.Succeeded ? result.Output : null, voice = speech.VoiceId, succeeded = result.Succeeded });
    }

    private static Task MarkAsync(
        IExecutionEventStore events,
        Guid traceId,
        Guid spanId,
        TimeProvider clock,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        return events.AppendAsync(
            new ExecutionEvent(
                Guid.NewGuid(), traceId, spanId, null, ExecutionOrigin.UserTurn, "dami-host",
                type, status, clock.GetUtcNow(), label),
            cancellationToken);
    }
}

/// <summary>What to say.</summary>
public sealed record SpeakRequest(string? Text);
