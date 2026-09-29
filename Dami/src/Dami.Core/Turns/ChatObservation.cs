using Dami.Contracts.Memory;

namespace Dami.Core.Turns;

/// <summary>
/// One exchange with Steve as the corpus keeps it: both halves bounded, the trace that
/// produced it attached.
/// </summary>
/// <remarks>
/// Both halves are bounded. The GUI tabs send their whole context dump as the request,
/// and a 13,000-character "Steve asked" is not an observation of Steve; it crowded the
/// weekly reflection out of its own prompt (2026-09-13).
/// </remarks>
public static class ChatObservation
{
    /// <summary>The corpus source every exchange is recorded under.</summary>
    public const string SOURCE = "chat";

    private const int RECORDED_REQUEST_CHARS = 400;
    private const int RECORDED_ANSWER_CHARS = 240;

    /// <summary>
    /// Builds the observation for one answered exchange. <paramref name="channel"/> names
    /// where it happened, when that is not the runtime's own turn path.
    /// </summary>
    public static Observation Of(
        string request, string answer, Guid traceId, DateTimeOffset occurredAt, string? channel = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);

        var metadata = new Dictionary<string, string> { ["trace_id"] = traceId.ToString() };
        if (channel is not null)
        {
            metadata["channel"] = channel;
        }

        return new Observation(
            Guid.NewGuid(),
            occurredAt,
            SOURCE,
            $"Steve asked: {Bound(request, RECORDED_REQUEST_CHARS)} — Dami answered: {Bound(answer, RECORDED_ANSWER_CHARS)}",
            metadata);
    }

    private static string Bound(string text, int maximum) =>
        text.Length <= maximum ? text : text[..maximum] + "…";
}
