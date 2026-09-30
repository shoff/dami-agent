using Dami.Contracts.Privacy;
using Dami.Core.Reliability;
using Dami.Gateway.Discord;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Says, in Steve's DM, when a sidecar stops doing real work, and when it is back.</summary>
/// <remarks>
/// A smoke detector, not a muse (ADR-0014 §2; docs/agent-landscape-2026-09.md A4). On
/// 2026-09-27 the GPU sidecars were stopped by hand under a running dami-host and every
/// Discord turn failed for forty hours with nobody told; the daily notice would have said
/// so the next morning, this says so within twenty minutes. Two failed probes in a row are
/// an outage — one is a restart in progress. Lending the GPU with Pause Dami stops dami-host
/// too, so a deliberate pause never sounds this. Operational content: probe names and errors.
/// </remarks>
public sealed class DiscordOutageAlarm : BackgroundService
{
    private static readonly TimeSpan every = TimeSpan.FromMinutes(10);

    private readonly IReadOnlyList<ISidecarProbe> probes;
    private readonly IEgressChannel channel;
    private readonly DiscordOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<DiscordOutageAlarm> logger;
    private readonly Dictionary<string, int> failures = new(StringComparer.Ordinal);
    private readonly HashSet<string> alarmed = new(StringComparer.Ordinal);

    /// <summary>Creates the alarm.</summary>
    public DiscordOutageAlarm(
        IEnumerable<ISidecarProbe> probes, IEgressChannel channel, DiscordOptions options, TimeProvider clock,
        ILogger<DiscordOutageAlarm> logger)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.probes = [.. probes];
        this.channel = channel;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Probes every sidecar once and says whatever changed.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        if (this.options.CheckInConversationId.Length == 0)
        {
            return;
        }

        foreach (var probe in this.probes)
        {
            var failure = await ProbeAsync(probe, cancellationToken).ConfigureAwait(false);
            var said = failure is null ? this.Recovered(probe.Name) : this.Failed(probe.Name, failure);
            if (said is not null)
            {
                this.logger.LogWarning("Outage alarm: {Said}", said);
                await this.channel.SendAsync(
                    new OutboundContent(this.options.CheckInConversationId, said, ContentProvenance.Operational, Guid.NewGuid()),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                this.logger.LogWarning(exception, "Outage alarm tick failed");
            }

            await Task.Delay(every, this.clock, stoppingToken).ConfigureAwait(false);
        }
    }

    private static async Task<string?> ProbeAsync(ISidecarProbe probe, CancellationToken cancellationToken)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromMinutes(1));
            await probe.ProbeAsync(budget.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return exception.Message;
        }
    }

    private string? Failed(string name, string failure)
    {
        this.failures[name] = this.failures.GetValueOrDefault(name) + 1;
        return this.failures[name] >= 2 && this.alarmed.Add(name)
            ? $"🔥 {name} has failed real work twice in a row ({failure}). Turns that need it will fail until it is back: "
              + "run Resume Dami (tools/dami-up) or `docker start` the sidecar."
            : null;
    }

    private string? Recovered(string name)
    {
        this.failures[name] = 0;
        return this.alarmed.Remove(name) ? $"✅ {name} is back." : null;
    }
}
