namespace Dami.Gui;

/// <summary>One moment in a replayed pass, before it is placed on a track.</summary>
public sealed record PassMoment(DateTimeOffset At, string Type, string Label, string Status);

/// <summary>Lays a pass out on a time axis.</summary>
/// <remarks>
/// A pass replayed as rows is a log with colours on it: every line looks equally spaced
/// whether it followed the last one instantly or four seconds later. The interesting fact
/// about the scout's rate-limited pass is not that it made two egress calls — it is that
/// the second one left it waiting for most of the pass, and rows cannot show that.
///
/// So each event is placed on a shared track by *when* it happened, and sized by how long
/// the runtime spent before the next thing occurred. That is the browser-devtools
/// waterfall, and it works here for the same reason it works there: the gaps carry the
/// information.
///
/// Pure and in fixed pixels so the geometry is testable without a laid-out window; a
/// chart that can only be checked by looking at it is a chart whose mistakes ship.
/// </remarks>
public static class PassWaterfall
{
    /// <summary>Track width in the fixed drawing space.</summary>
    public const double TRACK = 300;

    /// <summary>Narrowest a bar may be, so an instantaneous event is still visible.</summary>
    public const double MIN_BAR = 3;

    /// <summary>Places every moment on the track, in order.</summary>
    public static IReadOnlyList<PassEvent> Build(IReadOnlyList<PassMoment> moments)
    {
        ArgumentNullException.ThrowIfNull(moments);
        if (moments.Count == 0)
        {
            return [];
        }

        var start = moments[0].At;
        var span = (moments[^1].At - start).TotalSeconds;
        return moments.Select((moment, index) => Place(moments, index, start, span)).ToList();
    }

    private static PassEvent Place(
        IReadOnlyList<PassMoment> moments,
        int index,
        DateTimeOffset start,
        double span)
    {
        var moment = moments[index];
        var offset = (moment.At - start).TotalSeconds;
        var until = index + 1 < moments.Count
            ? (moments[index + 1].At - moment.At).TotalSeconds
            : 0;

        // Held back from the very end of the track, because the last event of a pass sits
        // exactly on it: without this the clamp below squeezes that bar to nothing and the
        // final step — often the one saying what the pass concluded — is invisible.
        var left = span <= 0 ? 0 : Math.Min(offset / span * TRACK, TRACK - MIN_BAR);
        var width = span <= 0 ? MIN_BAR : Math.Max(MIN_BAR, until / span * TRACK);
        return new PassEvent(
            moment.At.ToLocalTime().ToString("HH:mm:ss"),
            index == 0 ? "start" : $"+{offset:0.0}s",
            moment.Type,
            moment.Label,
            moment.Status,
            left,

            // Never past the end of the track: a final event with nothing after it would
            // otherwise be given the whole remaining width and read as a long operation.
            Math.Min(width, TRACK - left),
            IsAlert(moment));
    }

    /// <remarks>
    /// A non-2xx answer is the case this view exists for. The scout's second feed came
    /// back 429 and lost half its sources, and every other surface in the system reported
    /// that pass as Completed — its event status, its run, its service. Status alone would
    /// never have shown it, so the HTTP code is read out of the label.
    /// </remarks>
    public static bool IsAlert(PassMoment moment)
    {
        ArgumentNullException.ThrowIfNull(moment);
        if (moment.Status is "Failed" or "Cancelled")
        {
            return true;
        }

        var answered = moment.Label.IndexOf("answered ", StringComparison.Ordinal);
        return answered >= 0
            && int.TryParse(moment.Label.AsSpan(answered + 9).Trim(), out var code)
            && code is < 200 or >= 300;
    }
}
