using Dami.Proactive.Network;
using Xunit;

namespace Dami.Proactive.Tests.Network;

public sealed class SubnetTests
{
    [Fact]
    public void Hosts_Should_Exclude_The_Network_And_Broadcast_Addresses()
    {
        // Neither is a host, and pinging the broadcast provokes replies from everything at
        // once, which reads as noise rather than discovery.
        var hosts = Subnet.Hosts("192.168.4.0/24");

        Assert.Equal(254, hosts.Count);
        Assert.Equal("192.168.4.1", hosts[0]);
        Assert.Equal("192.168.4.254", hosts[^1]);
        Assert.DoesNotContain("192.168.4.0", hosts);
        Assert.DoesNotContain("192.168.4.255", hosts);
    }

    [Fact]
    public void Hosts_Should_Span_A_Prefix_Wider_Than_One_Octet()
    {
        // This host is 192.168.4.45/22, so a /24 assumption would miss three quarters of
        // the network it is actually on.
        var hosts = Subnet.Hosts("192.168.4.45/22");

        Assert.Equal(1022, hosts.Count);
        Assert.Equal("192.168.4.1", hosts[0]);
        Assert.Equal("192.168.7.254", hosts[^1]);
    }

    [Fact]
    public void Hosts_Should_Ignore_The_Host_Bits_When_Finding_The_Range()
    {
        Assert.Equal(Subnet.Hosts("192.168.4.45/22"), Subnet.Hosts("192.168.6.200/22"));
    }

    [Theory]
    [InlineData("192.168.4.0/8")]
    [InlineData("10.0.0.1/12")]
    public void Hosts_Should_Refuse_A_Range_Too_Large_To_Sweep(string cidr)
    {
        // A typo must not start a scan of sixteen million addresses.
        Assert.Empty(Subnet.Hosts(cidr));
    }

    [Theory]
    [InlineData("not-an-address/24")]
    [InlineData("192.168.4.0")]
    [InlineData("192.168.4.0/99")]
    [InlineData("::1/64")]
    public void Hosts_Should_Refuse_Nonsense(string cidr)
    {
        Assert.Empty(Subnet.Hosts(cidr));
    }
}

public sealed class NeighbourParsingTests
{
    [Fact]
    public void ParseNeighbours_Should_Read_Address_And_Hardware_Address()
    {
        var table = """
            192.168.4.1 dev wlp133s0f0 lladdr c4:a8:16:4b:6e:94 REACHABLE
            192.168.4.23 dev wlp133s0f0 lladdr ea:cc:55:9e:c8:3f STALE
            """;

        var found = SystemNetworkProbe.ParseNeighbours(table);

        Assert.Equal(2, found.Count);
        Assert.Equal(("192.168.4.1", "c4:a8:16:4b:6e:94"), (found[0].Address, found[0].Mac));
    }

    [Fact]
    public void ParseNeighbours_Should_Skip_Entries_With_No_Hardware_Address()
    {
        // An address the kernel has given up on has no lladdr; it is not a device found.
        var table = "192.168.4.99 dev wlp133s0f0 FAILED\n";

        Assert.Empty(SystemNetworkProbe.ParseNeighbours(table));
    }

    [Fact]
    public void ParseNeighbours_Should_Skip_IPv6_Entries()
    {
        // The link-local entry is the same hardware behind a second address, and the rest
        // of the collector speaks IPv4.
        var table = "fe80::c6a8:16ff:fe4b:6e94 dev wlp133s0f0 lladdr c4:a8:16:4b:6e:94 router STALE\n";

        Assert.Empty(SystemNetworkProbe.ParseNeighbours(table));
    }

    [Fact]
    public void ParseNeighbours_Should_Tolerate_An_Empty_Table()
    {
        Assert.Empty(SystemNetworkProbe.ParseNeighbours(string.Empty));
    }
}

public sealed class LanDeviceTests
{
    [Fact]
    public void Describe_Should_Lead_With_The_Hardware_Address()
    {
        // The MAC leads because it is the identity the collector matches on: an address is
        // a lease and a name is a courtesy.
        var device = new LanDevice("192.168.4.26", "44:27:45:83:d0:1a", "LGwebOSTV-qQUU-1.local");

        Assert.StartsWith("44:27:45:83:d0:1a", device.Describe(), StringComparison.Ordinal);
        Assert.Equal("44:27:45:83:d0:1a", device.Describe().Split(' ')[0]);
    }

    [Fact]
    public void Describe_Should_Omit_An_Empty_Name_Rather_Than_Show_Empty_Brackets()
    {
        var device = new LanDevice("192.168.4.43", "04:7c:16:80:fb:76", string.Empty);

        Assert.Equal("04:7c:16:80:fb:76 at 192.168.4.43", device.Describe());
    }
}
