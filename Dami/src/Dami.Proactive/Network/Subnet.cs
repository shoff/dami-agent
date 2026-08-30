using System.Net;
using System.Net.Sockets;

namespace Dami.Proactive.Network;

/// <summary>Enumerates the addresses of an IPv4 subnet.</summary>
/// <remarks>
/// Split out and pure because off-by-one errors here are invisible in a screenshot and
/// expensive in practice: scanning one address too far is a packet to a neighbour's
/// network, and one too few silently never finds the device at the end of the range.
/// </remarks>
public static class Subnet
{
    /// <summary>Largest range this will enumerate, so a typo cannot start a /8 sweep.</summary>
    public const int MAX_HOSTS = 4096;

    /// <summary>
    /// Usable host addresses for <paramref name="cidr"/>, excluding network and broadcast.
    /// Empty when the input is not a sane IPv4 CIDR or the range is larger than
    /// <see cref="MAX_HOSTS"/>.
    /// </summary>
    public static IReadOnlyList<string> Hosts(string cidr)
    {
        ArgumentNullException.ThrowIfNull(cidr);

        var parts = cidr.Split('/');
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out var prefix)
            || prefix is < 8 or > 32)
        {
            return [];
        }

        var total = 1L << (32 - prefix);
        if (total - 2 > MAX_HOSTS || total < 4)
        {
            return [];
        }

        var start = ToUint(address) & (uint)(0xFFFFFFFF << (32 - prefix));
        var hosts = new List<string>((int)total - 2);

        // Skip the network address and the broadcast: neither is a host, and pinging the
        // broadcast provokes replies from everything at once, which reads as noise.
        for (var offset = 1L; offset < total - 1; offset++)
        {
            hosts.Add(ToAddress(start + (uint)offset));
        }

        return hosts;
    }

    private static uint ToUint(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static string ToAddress(uint value) =>
        $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
}
