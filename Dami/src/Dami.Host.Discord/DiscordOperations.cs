using System.Globalization;
using System.Text;
using Dami.Contracts.Proactive;

namespace Dami.Host.Discord;

/// <summary>What Dami can answer over Discord without touching the profile.</summary>
/// <remarks>
/// ADR-0024 allows "surfacings, board state, status, and non-memory answers" across a
/// channel. Everything here is read from structured runtime state and rendered directly:
/// no retrieval, no model, and therefore nothing of Steve's in the answer by construction
/// rather than by a judgement call about what an answer happens to contain.
///
/// This exists because the general path cannot serve Discord. Every <c>/turns</c> call
/// assembles context — the first live question retrieved 13 memories and 2 beliefs — so a
/// memory-informed answer is the normal case and refusing it correctly refuses everything.
/// A useful gateway needs questions that were never going to touch memory in the first
/// place.
/// </remarks>
public static class DiscordOperations
{
    /// <summary>Which operational question was asked, if any.</summary>
    public enum Intent
    {
        /// <summary>Not an operational question; it belongs on the general path.</summary>
        None,

        /// <summary>What the gateway can answer.</summary>
        Help,

        /// <summary>What the proactive tier has been doing.</summary>
        Status,
    }

    /// <summary>Classifies a message. Deliberately narrow — an unknown phrasing is None.</summary>
    /// <remarks>
    /// Prefix matching rather than anything cleverer, because a fuzzy match that guesses
    /// "status" from a personal question would route a memory-bearing query into the
    /// unguarded path. Being too narrow costs a retry; being too loose costs the boundary.
    /// </remarks>
    public static Intent Classify(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var text = message.Trim().TrimStart('!', '/').Trim().ToLowerInvariant();

        return text switch
        {
            "help" or "?" or "commands" => Intent.Help,
            "status" or "services" or "workers" or "tier" => Intent.Status,
            _ => Intent.None,
        };
    }

    /// <summary>What the gateway will answer.</summary>
    public static string Help() =>
        """
        **Dami over Discord.** Only operational questions cross this channel (ADR-0024):

        `status` — what the proactive tier has been doing
        `help` — this

        Anything else runs a full turn on the host. If the answer draws on local memory it
        stays there and you get the trace id instead, which `dami trace <id>` will replay.
        """;

    /// <summary>Renders the proactive tier's state.</summary>
    public static string Status(IReadOnlyList<ProactiveServiceHistory> services, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Count == 0)
        {
            return "No proactive service has ever recorded a pass.";
        }

        var report = new StringBuilder();
        report.Append(CultureInfo.InvariantCulture, $"**Proactive tier** — {services.Count} service(s)\n");

        foreach (var service in services)
        {
            report.Append(Line(service, now)).Append('\n');
        }

        var alerting = services.Count(service => service.HasAlerts);
        if (alerting > 0)
        {
            report.Append(CultureInfo.InvariantCulture, $"\n{alerting} service(s) have passes wanting a look.");
        }

        return report.ToString().TrimEnd();
    }

    private static string Line(ProactiveServiceHistory service, DateTimeOffset now)
    {
        var mark = service.HasAlerts ? "⚠" : "·";
        var age = Age(now - service.LastRanAt);
        var due = service.NextDueAt is { } next
            ? next <= now ? ", due now" : $", due in {Age(next - now)}"
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{mark} `{service.ServiceName}` {service.LastStatus.ToString().ToLowerInvariant()} "
            + $"{age} ago over {service.Runs} run(s){due}");
    }

    /// <summary>Compact duration — a chat line has no room for a timestamp.</summary>
    public static string Age(TimeSpan span)
    {
        var absolute = span < TimeSpan.Zero ? TimeSpan.Zero : span;

        return absolute switch
        {
            { TotalMinutes: < 1 } => "moments",
            { TotalHours: < 1 } => $"{(int)absolute.TotalMinutes} min",
            { TotalDays: < 1 } => $"{(int)absolute.TotalHours} h",
            _ => $"{(int)absolute.TotalDays} d",
        };
    }
}
