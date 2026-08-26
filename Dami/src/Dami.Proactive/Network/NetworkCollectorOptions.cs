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
        new WatchedService { Name = "ollama", Port = 11434 },
    ];
}
