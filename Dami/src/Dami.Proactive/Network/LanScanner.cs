using Microsoft.Extensions.Logging;

namespace Dami.Proactive.Network;

/// <summary>A device found on the LAN.</summary>
public sealed record LanDevice(string Address, string Mac, string Name)
{
    /// <summary>
    /// How the fact reads, and how it is parsed back. The MAC leads because it is the
    /// stable identity — an address is a lease and a name is a courtesy, but the hardware
    /// address is what makes a device the same device tomorrow.
    /// </summary>
    public string Describe() =>
        $"{this.Mac} at {this.Address}{(this.Name.Length > 0 ? $" ({this.Name})" : string.Empty)}";
}

/// <summary>Finds what is actually on the local network.</summary>
/// <remarks>
/// The collector before this only pinged a hardcoded list, so it reported two devices on a
/// network holding seventeen. Discovery is a sweep, because the ARP table only knows what
/// this host has already spoken to: on a quiet machine it holds the gateway and nothing
/// else. Ping first, then read the table the sweep populated.
///
/// LocalOnly by construction, like the rest of this collector — ICMP to addresses inside
/// this host's own subnet, no egress client anywhere near it.
/// </remarks>
public sealed class LanScanner
{
    private readonly INetworkProbe probe;
    private readonly ILogger<LanScanner> logger;

    /// <summary>Creates the scanner.</summary>
    public LanScanner(INetworkProbe probe, ILogger<LanScanner> logger)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(logger);
        this.probe = probe;
        this.logger = logger;
    }

    /// <summary>Sweeps <paramref name="cidr"/> and returns what answered, by address.</summary>
    public async Task<IReadOnlyList<LanDevice>> ScanAsync(
        string cidr,
        int parallelism,
        CancellationToken cancellationToken)
    {
        var hosts = Subnet.Hosts(cidr);
        if (hosts.Count == 0)
        {
            this.logger.LogWarning("LAN scan: {Cidr} is not a scannable range", cidr);
            return [];
        }

        var alive = await this.SweepAsync(hosts, parallelism, cancellationToken).ConfigureAwait(false);
        var neighbours = (await this.probe.NeighboursAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(entry => entry.Address, entry => entry.Mac, StringComparer.Ordinal);

        var devices = new List<LanDevice>();
        foreach (var address in alive)
        {
            var name = await this.probe.ResolveAsync(address, cancellationToken).ConfigureAwait(false);
            devices.Add(new LanDevice(
                address, neighbours.GetValueOrDefault(address, string.Empty), name ?? string.Empty));
        }

        this.logger.LogInformation(
            "LAN scan: {Alive} of {Scanned} addresses answered", devices.Count, hosts.Count);
        return devices;
    }

    /// <remarks>
    /// Bounded concurrency rather than one task per address: a /22 is a thousand pings, and
    /// launching them all at once buries the host's own network stack in the noise it is
    /// trying to measure.
    /// </remarks>
    private async Task<IReadOnlyList<string>> SweepAsync(
        IReadOnlyList<string> hosts,
        int parallelism,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, parallelism));
        var answered = new List<string>();
        var guard = new Lock();

        await Task.WhenAll(hosts.Select(async address =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (await this.probe.PingAsync(address, cancellationToken).ConfigureAwait(false) is not null)
                {
                    lock (guard)
                    {
                        answered.Add(address);
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        return answered.Order(StringComparer.Ordinal).ToList();
    }
}
