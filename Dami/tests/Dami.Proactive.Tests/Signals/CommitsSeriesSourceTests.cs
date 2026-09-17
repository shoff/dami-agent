using Dami.Contracts.Domains;
using Dami.Proactive.CodeAudit;
using Dami.Proactive.Signals;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Signals;

public sealed class CommitsSeriesSourceTests
{
    private static readonly TimeZoneInfo chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private readonly IGitLog gitLog = Substitute.For<IGitLog>();

    [Fact]
    public async Task ReadAsync_Should_Count_Commits_Per_Local_Day()
    {
        this.gitLog.CommitTimesAsync("/repo", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<DateTimeOffset>
            {
                new(2026, 9, 15, 2, 0, 0, TimeSpan.Zero),   // evening of the 14th in Chicago
                new(2026, 9, 14, 18, 0, 0, TimeSpan.Zero),
                new(2026, 9, 13, 18, 0, 0, TimeSpan.Zero),
            });

        var series = await this.CreateSource().ReadAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), chicago, CancellationToken.None);

        Assert.Equal(new DailyPoint(new DateOnly(2026, 9, 14), 2.0), series[0].Points[1]);
    }

    [Fact]
    public async Task ReadAsync_Should_Ask_Git_From_The_Windows_Start()
    {
        this.gitLog.CommitTimesAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<DateTimeOffset>());

        await this.CreateSource().ReadAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), chicago, CancellationToken.None);

        await this.gitLog.Received(1).CommitTimesAsync(
            "/repo", Arg.Is<DateTimeOffset>(since => since < new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero)),
            Arg.Any<CancellationToken>());
    }

    private CommitsSeriesSource CreateSource() =>
        new(this.gitLog, Options.Create(new SignalsOptions { RepoPath = "/repo" }));
}
