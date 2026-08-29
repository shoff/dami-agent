using Dami.Contracts.Proactive;

namespace Dami.Host;

/// <summary>What the proactive tier has been doing, for anything that shows a person.</summary>
/// <remarks>
/// The tier runs unattended on an hourly tick in its own process and writes to
/// <c>dami.proactive_runs</c>. Nothing surfaced that: three of its eleven services had not
/// run since 2026-08-23 and no interface could say whether that was their cadence or a
/// fault. This is read-only by design — starting or stopping a service is the operator's
/// act through systemd or <c>Dami.Host.Proactive --run</c>, not a web call.
/// </remarks>
internal static class ProactiveEndpoints
{
    private const int DEFAULT_RECENT = 10;
    private const int MAX_RECENT = 50;

    /// <summary>Maps the proactive read surface.</summary>
    internal static void MapDamiProactive(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/proactive", ReadAsync);
    }

    private static async Task<IResult> ReadAsync(
        IProactiveRunHistory history,
        TimeProvider clock,
        int? recent,
        CancellationToken cancellationToken)
    {
        var requested = recent ?? DEFAULT_RECENT;
        if (requested is <= 0 or > MAX_RECENT)
        {
            return Results.BadRequest(new { error = $"recent must be between 1 and {MAX_RECENT}" });
        }

        var services = await history.ReadAsync(requested, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        return Results.Ok(services.Select(service => new
        {
            serviceName = service.ServiceName,
            runs = service.Runs,
            lastRanAt = service.LastRanAt,
            lastStatus = service.LastStatus.ToString(),

            // The panel's whole job is "is this stale?", and a client computing that from
            // two clocks gets it wrong the moment they disagree. Answer it here.
            sinceLastRunHours = Math.Round((now - service.LastRanAt).TotalHours, 1),
            recent = service.Recent.Select(run => new
            {
                runId = run.RunId,
                traceId = run.TraceId,
                ranAt = run.RanAt,
                status = run.Status.ToString(),
            }),
        }));
    }
}
