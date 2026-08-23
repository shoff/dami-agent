using Dami.Contracts.Memory;
using Dami.Proactive.Audit;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Audit;

/// <summary>The decay detector: always concludes, surfaces only on a material drop.</summary>
public sealed class PushbackAuditServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 4, 0, 0, TimeSpan.Zero);

    private readonly IPushbackLedger pushbackLedger = Substitute.For<IPushbackLedger>();

    [Fact]
    public async Task RunPassAsync_Should_Always_Record_A_Conclusion()
    {
        this.ArrangeQuarters(current: 5, previous: 6);

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Conclusions);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_When_The_Rate_Holds()
    {
        this.ArrangeQuarters(current: 5, previous: 6);

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Surface_A_Material_Drop()
    {
        this.ArrangeQuarters(current: 2, previous: 10);

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_With_No_Baseline()
    {
        this.ArrangeQuarters(current: 0, previous: 0);

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Attribute_The_Conclusion_To_SelfAudit()
    {
        this.ArrangeQuarters(current: 3, previous: 3);

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(ConclusionSource.SelfAudit, result.Conclusions[0].Source);
    }

    private void ArrangeQuarters(int current, int previous)
    {
        this.pushbackLedger.RateAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var from = callInfo.ArgAt<DateTimeOffset>(0);
                var total = from >= now.AddDays(-92) ? current : previous;
                return new PushbackRate(from, callInfo.ArgAt<DateTimeOffset>(1), total, 0, 0, 0, total);
            });
    }

    private static ProactiveContext Context()
    {
        return new ProactiveContext(Guid.NewGuid(), now, null);
    }

    private PushbackAuditService CreateService()
    {
        return new PushbackAuditService(
            this.pushbackLedger, new FakeTimeProvider(now), NullLogger<PushbackAuditService>.Instance);
    }
}
