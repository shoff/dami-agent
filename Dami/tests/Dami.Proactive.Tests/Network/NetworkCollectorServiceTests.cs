using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Dami.Proactive.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Network;

public sealed class NetworkCollectorServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 25, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunPassAsync_Should_Write_One_Dated_Fact_Per_Interface_Gateway_Host_And_Service()
    {
        var probe = Probe();
        var store = Substitute.For<IDomainFactStore>();
        var written = new List<DomainFact>();
        store.RecordAsync(Arg.Do<DomainFact>(written.Add), Arg.Any<CancellationToken>()).Returns(true);
        var service = new NetworkCollectorService(
            store, probe, Options.Create(TwoServices()), new FakeTimeProvider(now), NullLogger<NetworkCollectorService>.Instance);

        var result = await service.RunPassAsync(new ProactiveContext(Guid.NewGuid(), now, null), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Completed, result.Status);
        Assert.All(written, fact => Assert.Equal(("network", new DateOnly(2026, 8, 25), "network-collector"), (fact.Domain, fact.AsOf, fact.Source)));
        Assert.Equal(
            [
                "Interface wlp133s0f0 is up (192.168.4.45/22)",
                "Interface eno1 is down (no IPv4 address)",
                "Default gateway is 192.168.4.1",
                "gateway (192.168.4.1) answers ping",
                "mac-mini (192.168.4.23) does not answer ping",
                "postgresql on 127.0.0.1:5432 is listening",
                "ollama on 127.0.0.1:11434 is not listening",
            ],
            written.Select(fact => fact.Description));
    }

    private static INetworkProbe Probe()
    {
        var probe = Substitute.For<INetworkProbe>();
        probe.Interfaces().Returns([
            new InterfaceState("wlp133s0f0", true, ["192.168.4.45/22"]),
            new InterfaceState("eno1", false, []),
        ]);
        probe.Gateway().Returns("192.168.4.1");
        probe.PingAsync("192.168.4.1", Arg.Any<CancellationToken>()).Returns(2L);
        probe.PingAsync("192.168.4.23", Arg.Any<CancellationToken>()).Returns((long?)null);
        probe.ListeningAsync(5432, Arg.Any<CancellationToken>()).Returns(true);
        probe.ListeningAsync(Arg.Is<int>(port => port != 5432), Arg.Any<CancellationToken>()).Returns(false);
        return probe;
    }

    private static NetworkCollectorOptions TwoServices()
    {
        var options = new NetworkCollectorOptions();
        options.Services.Clear();
        options.Services.Add(new WatchedService { Name = "postgresql", Port = 5432 });
        options.Services.Add(new WatchedService { Name = "ollama", Port = 11434 });
        return options;
    }
}
