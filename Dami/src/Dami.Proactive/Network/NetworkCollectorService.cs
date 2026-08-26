using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Network;

/// <summary>The network domain, from this host's own state (K4, first domain after health).</summary>
/// <remarks>
/// LocalOnly by construction: it reads the host's interfaces, pings the LAN, and knocks on
/// loopback ports. Nothing here has an egress client. Each pass writes one fact per
/// observation per day, so a state that holds is a row a day (the timeline) and a state
/// that changes is visible as the day it changed. It surfaces nothing; reflection reads it.
/// </remarks>
public sealed class NetworkCollectorService : IProactiveService
{
    private const string DOMAIN = "network";
    private const string SOURCE = "network-collector";

    private readonly IDomainFactStore store;
    private readonly INetworkProbe probe;
    private readonly NetworkCollectorOptions collectorOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<NetworkCollectorService> logger;

    /// <summary>Creates the service.</summary>
    public NetworkCollectorService(
        IDomainFactStore store,
        INetworkProbe probe,
        IOptions<NetworkCollectorOptions> collectorOptions,
        TimeProvider clock,
        ILogger<NetworkCollectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(collectorOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.probe = probe;
        this.collectorOptions = collectorOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "network-collector";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Nightly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(ProactiveContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var facts = new List<(string Category, string Description)>();
        facts.AddRange(this.InterfaceFacts());
        facts.AddRange(await this.ReachabilityFactsAsync(cancellationToken).ConfigureAwait(false));
        facts.AddRange(await this.ServiceFactsAsync(cancellationToken).ConfigureAwait(false));

        var now = this.clock.GetUtcNow();
        var written = 0;
        foreach (var (category, description) in facts)
        {
            var fact = new DomainFact(
                Guid.NewGuid(), DOMAIN, DateOnly.FromDateTime(now.UtcDateTime), category, description, SOURCE, now);
            written += await this.store.RecordAsync(fact, cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        this.logger.LogInformation("Network collector: {Written} new fact(s) of {Observed}", written, facts.Count);
        return ProactiveResult.quiet;
    }

    private IEnumerable<(string, string)> InterfaceFacts()
    {
        foreach (var nic in this.probe.Interfaces())
        {
            var addresses = nic.Addresses.Count == 0 ? "no IPv4 address" : string.Join(", ", nic.Addresses);
            yield return ("interface", $"Interface {nic.Name} is {(nic.IsUp ? "up" : "down")} ({addresses})");
        }

        var gateway = this.probe.Gateway();
        yield return ("gateway", gateway is null ? "No default gateway is configured" : $"Default gateway is {gateway}");
    }

    private async Task<List<(string, string)>> ReachabilityFactsAsync(CancellationToken cancellationToken)
    {
        var facts = new List<(string, string)>();
        var gateway = this.probe.Gateway();
        if (gateway is not null)
        {
            facts.Add(("reachability", await this.ReachAsync("gateway", gateway, cancellationToken).ConfigureAwait(false)));
        }

        foreach (var host in this.collectorOptions.Hosts)
        {
            facts.Add(("reachability", await this.ReachAsync(host.Name, host.Address, cancellationToken).ConfigureAwait(false)));
        }

        return facts;
    }

    private async Task<string> ReachAsync(string name, string address, CancellationToken cancellationToken)
    {
        var roundTrip = await this.probe.PingAsync(address, cancellationToken).ConfigureAwait(false);
        return roundTrip is null
            ? $"{name} ({address}) does not answer ping"
            : $"{name} ({address}) answers ping";
    }

    private async Task<List<(string, string)>> ServiceFactsAsync(CancellationToken cancellationToken)
    {
        var facts = new List<(string, string)>();
        foreach (var service in this.collectorOptions.Services)
        {
            var listening = await this.probe.ListeningAsync(service.Port, cancellationToken).ConfigureAwait(false);
            facts.Add(("service", $"{service.Name} on 127.0.0.1:{service.Port} is {(listening ? "listening" : "not listening")}"));
        }

        return facts;
    }
}
