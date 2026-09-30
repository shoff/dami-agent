using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Dami.Proactive.Security;

/// <summary>A TCP socket listening on an address other than loopback.</summary>
public sealed record Listener(IPAddress Address, int Port)
{
    /// <inheritdoc />
    public override string ToString() =>
        this.Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{this.Address}]:{this.Port}" : $"{this.Address}:{this.Port}";
}

/// <summary>Reads the kernel's TCP tables for listening sockets reachable from off this host.</summary>
public static class ExposedListeners
{
    private const string LISTEN = "0A";

    /// <summary>The listeners in <c>/proc/net/tcp</c> and <c>/proc/net/tcp6</c> text, not on loopback, by port.</summary>
    public static IReadOnlyList<Listener> Parse(string tcp, string tcp6)
    {
        ArgumentNullException.ThrowIfNull(tcp);
        ArgumentNullException.ThrowIfNull(tcp6);
        return [.. Rows(tcp).Concat(Rows(tcp6))
            .Where(listener => !IPAddress.IsLoopback(listener.Address) && !IsMappedLoopback(listener.Address))
            .Distinct()
            .OrderBy(listener => listener.Port)
            .ThenBy(listener => listener.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>This host's own tables.</summary>
    public static IReadOnlyList<Listener> ReadThisHost() =>
        Parse(ReadOrEmpty("/proc/net/tcp"), ReadOrEmpty("/proc/net/tcp6"));

    private static string ReadOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static IEnumerable<Listener> Rows(string table)
    {
        foreach (var line in table.Split('\n').Skip(1))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length > 3 && fields[3] == LISTEN && fields[1].Split(':') is [var address, var port])
            {
                yield return new Listener(Address(address), int.Parse(port, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>The kernel writes each 32-bit word of the address in host (little-endian) order.</summary>
    private static IPAddress Address(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        for (var word = 0; word < bytes.Length; word += 4)
        {
            Array.Reverse(bytes, word, 4);
        }

        return new IPAddress(bytes);
    }

    private static bool IsMappedLoopback(IPAddress address) =>
        address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4());
}
