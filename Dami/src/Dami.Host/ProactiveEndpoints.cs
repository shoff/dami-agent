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
        return Results.Ok(services.Select(service => Describe(service, now)));
    }

    /// <remarks>
    /// Staleness and due-ness are answered here, from one clock. A client computing them
    /// from its own gets a different answer the moment the two disagree — which on this
    /// host they have. Staleness is also only meaningful against a cadence: four services
    /// reading "1 run, 5 days ago" looked stuck and were simply Weekly and Quarterly.
    /// Cadence is null for runs recorded before migration 035 — unknown, never a guess —
    /// and dueInHours goes negative once a service is overdue.
    /// </remarks>
    private static object Describe(ProactiveServiceHistory service, DateTimeOffset now)
    {
        return new
        {
            serviceName = service.ServiceName,
            runs = service.Runs,
            lastRanAt = service.LastRanAt,
            lastStatus = service.LastStatus.ToString(),
            sinceLastRunHours = Math.Round((now - service.LastRanAt).TotalHours, 1),
            cadence = service.Cadence?.ToString(),
            nextDueAt = service.NextDueAt,
            dueInHours = service.NextDueAt is { } due
                ? Math.Round((due - now).TotalHours, 1)
                : (double?)null,
            totalProduced = service.TotalProduced,
            totalEgress = service.TotalEgress,
            totalAlerts = service.TotalAlerts,
            recent = service.Recent.Select(run => new
            {
                runId = run.RunId,
                traceId = run.TraceId,
                ranAt = run.RanAt,
                status = run.Status.ToString(),
                produced = run.Produced,
                egress = run.Egress,
                alerts = run.Alerts,
                seconds = Math.Round(run.Seconds, 1),
                events = run.Events,
            }),
        };
    }
}
