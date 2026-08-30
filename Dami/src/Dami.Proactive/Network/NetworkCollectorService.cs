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
    private const string DEVICE = "device";
    private const int MAX_HISTORY = 5000;

    private readonly IDomainFactStore store;
    private readonly INetworkProbe probe;
    private readonly LanScanner scanner;
    private readonly NetworkCollectorOptions collectorOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<NetworkCollectorService> logger;

    /// <summary>Creates the service.</summary>
    public NetworkCollectorService(
        IDomainFactStore store,
        INetworkProbe probe,
        LanScanner scanner,
        IOptions<NetworkCollectorOptions> collectorOptions,
        TimeProvider clock,
        ILogger<NetworkCollectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(collectorOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.probe = probe;
        this.scanner = scanner;
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

        var devices = await this.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        facts.AddRange(devices.Select(device => (DEVICE, device.Describe())));

        var now = this.clock.GetUtcNow();
        var written = 0;
        foreach (var (category, description) in facts)
        {
            var fact = new DomainFact(
                Guid.NewGuid(), DOMAIN, DateOnly.FromDateTime(now.UtcDateTime), category, description, SOURCE, now);
            written += await this.store.RecordAsync(fact, cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        this.logger.LogInformation("Network collector: {Written} new fact(s) of {Observed}", written, facts.Count);

        var unfamiliar = await this.UnfamiliarAsync(devices, cancellationToken).ConfigureAwait(false);
        var note = $"{written} new fact(s) of {facts.Count}"
            + (devices.Count > 0 ? $", {devices.Count} device(s) on the network" : string.Empty);

        return unfamiliar.Count == 0
            ? ProactiveResult.Did(note)
            : new ProactiveResult(
                [], [this.Surface(unfamiliar)], ProactiveStatus.Completed,
                $"{note}, {unfamiliar.Count} not seen before");
    }

    /// <summary>Sweeps the subnet, unless discovery is switched off.</summary>
    /// <remarks>
    /// The range defaults to the subnet of the first interface that is up rather than a
    /// configured constant, so the answer stays right when DHCP moves this host.
    /// </remarks>
    private async Task<IReadOnlyList<LanDevice>> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (!this.collectorOptions.DiscoverDevices)
        {
            return [];
        }

        var cidr = this.collectorOptions.Cidr.Length > 0
            ? this.collectorOptions.Cidr
            : this.probe.Interfaces()
                .Where(nic => nic.IsUp)
                .SelectMany(nic => nic.Addresses)
                .FirstOrDefault(string.Empty);

        return cidr.Length == 0
            ? []
            : await this.scanner
                .ScanAsync(cidr, this.collectorOptions.ScanParallelism, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Devices whose hardware address has not appeared in the domain's recent history.
    /// </summary>
    /// <remarks>
    /// The MAC is the identity, not the address: DHCP moves a device between leases and a
    /// name is whatever it chooses to advertise, so matching on either would report the
    /// same laptop as new every time the router reshuffled.
    /// </remarks>
    private async Task<IReadOnlyList<LanDevice>> UnfamiliarAsync(
        IReadOnlyList<LanDevice> devices,
        CancellationToken cancellationToken)
    {
        var candidates = devices.Where(device => device.Mac.Length > 0).ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(this.clock.GetUtcNow().UtcDateTime);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var fact in this.store.BetweenAsync(
            DOMAIN, today.AddDays(-this.collectorOptions.KnownDeviceDays), today.AddDays(-1),
            MAX_HISTORY, cancellationToken).ConfigureAwait(false))
        {
            if (fact.Category == DEVICE)
            {
                known.Add(fact.Description.Split(' ')[0]);
            }
        }

        return candidates.Where(device => !known.Contains(device.Mac)).ToList();
    }

    private Surfacing Surface(IReadOnlyList<LanDevice> devices)
    {
        var headline = devices.Count == 1
            ? $"A device you have not seen before is on the network: {Name(devices[0])}"
            : $"{devices.Count} devices you have not seen before are on the network";

        return new Surfacing(
            Guid.NewGuid(),
            SOURCE,
            headline,
            string.Join("\n", devices.Select(device => $"· {device.Describe()}")),
            0.8,
            this.clock.GetUtcNow());
    }

    private static string Name(LanDevice device) =>
        device.Name.Length > 0 ? device.Name : $"{device.Mac} at {device.Address}";

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
