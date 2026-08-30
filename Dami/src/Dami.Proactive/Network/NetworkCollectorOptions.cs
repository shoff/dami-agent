namespace Dami.Proactive.Network;

/// <summary>A LAN host worth checking by name.</summary>
public sealed class WatchedHost
{
    /// <summary>What to call it in a fact.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Its address.</summary>
    public string Address { get; set; } = string.Empty;
}

/// <summary>A loopback service worth checking by port.</summary>
public sealed class WatchedService
{
    /// <summary>What to call it in a fact.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Its port on loopback.</summary>
    public int Port { get; set; }
}

/// <summary>What the network collector watches. Defaults describe this workstation; edit them.</summary>
public sealed class NetworkCollectorOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "NetworkCollector";

    /// <summary>Sweep the local subnet to find what is actually on it.</summary>
    /// <remarks>
    /// On by default. Without it the collector reports only the hosts it was told about,
    /// which on this network meant two devices out of seventeen. The sweep is ICMP inside
    /// this host's own subnet and nothing else.
    /// </remarks>
    public bool DiscoverDevices { get; set; } = true;

    /// <summary>
    /// The range to sweep. Empty means the subnet of the first up interface, which is the
    /// answer that stays right when DHCP moves the host.
    /// </summary>
    public string Cidr { get; set; } = string.Empty;

    /// <summary>Concurrent pings. Enough to finish a /22 in seconds, few enough not to drown the NIC.</summary>
    public int ScanParallelism { get; set; } = 128;

    /// <summary>How far back to look before calling a device new.</summary>
    /// <remarks>
    /// A device seen last month and absent since is not news when it comes back; a device
    /// never seen before is. Too short a window makes every laptop that went on holiday a
    /// surfacing, which is how an inbox gets ignored.
    /// </remarks>
    public int KnownDeviceDays { get; set; } = 60;

    /// <summary>LAN hosts to reach. Default: the Mac mini the corpus keeps mentioning.</summary>
    public IList<WatchedHost> Hosts { get; } =
    [
        new WatchedHost { Name = "mac-mini", Address = "192.168.4.23" },
    ];

    /// <summary>Loopback services that should be listening.</summary>
    public IList<WatchedService> Services { get; } =
    [
        new WatchedService { Name = "postgresql", Port = 5432 },
        new WatchedService { Name = "dami-host", Port = 5810 },
        new WatchedService { Name = "tei-embed", Port = 8080 },
        new WatchedService { Name = "tei-rerank", Port = 8081 },
        new WatchedService { Name = "dami-stt", Port = 8090 },
        new WatchedService { Name = "dami-tts", Port = 8091 },
        new WatchedService { Name = "ollama", Port = 11434 },
    ];
}
