using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Workers;

namespace Dami.Host;

/// <summary>Speech to text (L3), run as a bounded worker under a real trace.</summary>
public static class TranscriptionEndpoints
{
    private static readonly TimeSpan bound = TimeSpan.FromMinutes(5);

    /// <summary>Maps the transcription route.</summary>
    public static void Map(WebApplication app)
    {
        app.MapPost("/transcribe", TranscribeAsync);
    }

    private static async Task<IResult> TranscribeAsync(
        TranscribeRequest request,
        ITranscriptionClient transcription,
        IWorkerRunner workers,
        IExecutionEventStore events,
        TimeProvider clock,
        CancellationToken token)
    {
        if (!TryDecode(request.AudioBase64, out var audio))
        {
            return Results.BadRequest(new { error = "audioBase64 is missing or not valid base64" });
        }

        var traceId = Guid.NewGuid();
        var rootSpanId = Guid.NewGuid();
        await MarkAsync(events, traceId, rootSpanId, clock, ExecutionEventType.TraceStarted,
            ExecutionStatus.Running, $"transcribe {audio.Length} bytes", token).ConfigureAwait(false);

        var result = await workers.RunAsync(
            "speech-to-text", traceId, rootSpanId, bound,
            inner => transcription.TranscribeAsync(audio, "clip.wav", inner),
            token).ConfigureAwait(false);

        await MarkAsync(events, traceId, rootSpanId, clock,
            result.Succeeded ? ExecutionEventType.TraceCompleted : ExecutionEventType.TraceFailed,
            result.Succeeded ? ExecutionStatus.Succeeded : ExecutionStatus.Failed,
            $"transcribe {(result.Succeeded ? "done" : "failed")}", token).ConfigureAwait(false);

        return Results.Ok(new
        {
            traceId,
            text = result.Output,
            model = transcription.ModelId,
            succeeded = result.Succeeded,
        });
    }

    private static bool TryDecode(string? base64, out byte[] audio)
    {
        audio = [];
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        try
        {
            audio = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return false;
        }

        return audio.Length > 0;
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

/// <summary>One audio clip, base64 encoded. Loopback only, so the encoding cost is free.</summary>
public sealed record TranscribeRequest(string? AudioBase64);
