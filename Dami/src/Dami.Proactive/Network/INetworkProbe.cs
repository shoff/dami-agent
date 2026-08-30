using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Dami.Proactive.Network;

/// <summary>One network interface as the collector sees it.</summary>
public sealed record InterfaceState(string Name, bool IsUp, IReadOnlyList<string> Addresses);

/// <summary>An address the kernel has an ARP entry for.</summary>
public sealed record Neighbour(string Address, string Mac);

/// <summary>What the host can find out about its own network without leaving it.</summary>
public interface INetworkProbe
{
    /// <summary>The kernel's ARP table: addresses this host has actually talked to.</summary>
    /// <remarks>
    /// Read after a sweep, not before. The table only knows what has been spoken to, so on
    /// a quiet host it holds the gateway and nothing else.
    /// </remarks>
    Task<IReadOnlyList<Neighbour>> NeighboursAsync(CancellationToken cancellationToken);

    /// <summary>A name for an address, or null. Resolves mDNS too where nss-mdns is set up.</summary>
    Task<string?> ResolveAsync(string address, CancellationToken cancellationToken);

    /// <summary>Non-loopback interfaces and their addresses.</summary>
    IReadOnlyList<InterfaceState> Interfaces();

    /// <summary>The default gateway, if one is configured.</summary>
    string? Gateway();

    /// <summary>Round trip in milliseconds, or null when the address does not answer.</summary>
    Task<long?> PingAsync(string address, CancellationToken cancellationToken);

    /// <summary>Whether a TCP port on loopback accepts a connection.</summary>
    Task<bool> ListeningAsync(int port, CancellationToken cancellationToken);
}

/// <summary>The real thing: .NET's network stack, loopback and LAN only.</summary>
public sealed class SystemNetworkProbe : INetworkProbe
{
    private const int TIMEOUT_MS = 1500;

    /// <inheritdoc />
    public IReadOnlyList<InterfaceState> Interfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback && !IsContainerPlumbing(nic.Name))
            .Select(nic => new InterfaceState(
                nic.Name,
                nic.OperationalStatus == OperationalStatus.Up,
                [.. nic.GetIPProperties().UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => $"{address.Address}/{address.PrefixLength}")]))
            .OrderBy(nic => nic.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Docker's bridges and veth pairs come and go with containers; they are not the network.</summary>
    private static bool IsContainerPlumbing(string name)
    {
        return name.StartsWith("veth", StringComparison.Ordinal)
            || name.StartsWith("br-", StringComparison.Ordinal)
            || name.StartsWith("docker", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public string? Gateway()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .SelectMany(nic => nic.GetIPProperties().GatewayAddresses)
            .Select(gateway => gateway.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)?
            .ToString();
    }

    /// <inheritdoc />
    public async Task<long?> PingAsync(string address, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(TIMEOUT_MS), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Shells out to <c>ip</c> because .NET exposes no ARP table. Read-only, and a
    /// non-zero exit is treated as "nothing known" rather than a fault: an empty table is
    /// a legitimate answer on a host that has not spoken to anyone.
    /// </remarks>
    public async Task<IReadOnlyList<Neighbour>> NeighboursAsync(CancellationToken cancellationToken)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ip",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        process.StartInfo.ArgumentList.Add("neigh");
        process.StartInfo.ArgumentList.Add("show");

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0 ? ParseNeighbours(output) : [];
    }

    /// <summary>Parses <c>ip neigh show</c>: "ADDR dev IF lladdr MAC STATE".</summary>
    public static IReadOnlyList<Neighbour> ParseNeighbours(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var found = new List<Neighbour>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lladdr = Array.IndexOf(fields, "lladdr");

            // IPv6 link-local entries duplicate the same hardware behind a different
            // address; the IPv4 view is the one the rest of the collector speaks.
            if (lladdr < 0 || lladdr + 1 >= fields.Length || fields[0].Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            found.Add(new Neighbour(fields[0], fields[lladdr + 1]));
        }

        return found;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(string address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        try
        {
            var entry = await Dns.GetHostEntryAsync(address, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(entry.HostName) || entry.HostName == address
                ? null
                : entry.HostName;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ListeningAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TIMEOUT_MS);
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The connect timed out; the caller did not cancel.
            return false;
        }
    }
}
