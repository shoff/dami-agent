using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Dami.Proactive.Network;

/// <summary>One network interface as the collector sees it.</summary>
public sealed record InterfaceState(string Name, bool IsUp, IReadOnlyList<string> Addresses);

/// <summary>What the host can find out about its own network without leaving it.</summary>
public interface INetworkProbe
{
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
            .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(nic => new InterfaceState(
                nic.Name,
                nic.OperationalStatus == OperationalStatus.Up,
                [.. nic.GetIPProperties().UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => $"{address.Address}/{address.PrefixLength}")]))
            .OrderBy(nic => nic.Name, StringComparer.Ordinal)
            .ToArray();
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
