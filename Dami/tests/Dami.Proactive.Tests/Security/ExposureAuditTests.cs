using Dami.Contracts.Proactive;
using Dami.Proactive.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dami.Proactive.Tests.Security;

/// <summary>
/// What this host listens on beyond loopback, against what the runbook says it should
/// (docs/agent-landscape-2026-09.md H5; ~21k exposed OpenClaw instances were counted in 2026).
/// </summary>
public sealed class ExposureAuditTests
{
    private const string TCP = """
          sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
           0: 00000000:0016 00000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 1 1 0 100 0 0 10 0
           1: 0100007F:1538 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 2 1 0 100 0 0 10 0
           2: 00000000:1538 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 3 1 0 100 0 0 10 0
           3: 2D04A8C0:D0C2 2D04A8C0:0016 01 00000000:00000000 00:00000000 00000000  1000        0 4 1 0 100 0 0 10 0
        """;

    private const string TCP6 = """
          sl  local_address                         remote_address                        st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
           0: 00000000000000000000000000000000:20FB 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 5 1 0 100 0 0 10 0
           1: 00000000000000000000000001000000:1F90 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 6 1 0 100 0 0 10 0
        """;

    [Fact]
    public void Only_Listening_Sockets_Beyond_Loopback_Should_Count()
    {
        var exposed = ExposedListeners.Parse(TCP, TCP6);

        Assert.Equal(["0.0.0.0:22", "0.0.0.0:5432", "[::]:8443"], exposed.Select(listener => listener.ToString()));
    }

    [Fact]
    public async Task An_Unexpected_Listener_Should_Surface_And_Expected_Ones_Should_Not()
    {
        var service = new ExposureAuditService(
            () => ExposedListeners.Parse(TCP, TCP6), Options.Create(new ExposureAuditOptions()),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 29, 20, 0, 0, TimeSpan.Zero)), NullLogger<ExposureAuditService>.Instance);

        var result = await service.RunPassAsync(new ProactiveContext(Guid.NewGuid(), DateTimeOffset.UnixEpoch, null), CancellationToken.None);

        var surfacing = Assert.Single(result.Surfacings);
        Assert.Contains("0.0.0.0:5432", surfacing.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(":22", surfacing.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("8443", surfacing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Host_Listening_Only_Where_Expected_Should_Be_Quiet()
    {
        var service = new ExposureAuditService(
            () => ExposedListeners.Parse(TCP.Replace("00000000:1538 00000000:0000 0A", "0100007F:1538 00000000:0000 0A", StringComparison.Ordinal), TCP6),
            Options.Create(new ExposureAuditOptions()), TimeProvider.System, NullLogger<ExposureAuditService>.Instance);

        var result = await service.RunPassAsync(new ProactiveContext(Guid.NewGuid(), DateTimeOffset.UnixEpoch, null), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }
}
