using Dami.Contracts.Domains;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Civic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Civic;

public sealed class CivicFeedCollectorServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 25, 22, 0, 0, TimeSpan.Zero);

    private const string RSS = """
        <rss version="2.0"><channel><title>Lakeville, MN - Calendar</title>
        <item><title>Finance Committee Meeting</title><link>https://www.lakevillemn.gov/calendar.aspx?EID=1</link><pubDate>Wed, 26 Aug 2026 00:00:00 GMT</pubDate></item>
        <item><title>City Council Meeting</title><link>https://www.lakevillemn.gov/calendar.aspx?EID=2</link></item>
        </channel></rss>
        """;

    [Fact]
    public async Task RunPassAsync_Should_Turn_Feed_Items_Into_Dated_Civic_Facts_And_Survive_A_Refused_Feed()
    {
        var egress = Substitute.For<IEgressClient>();
        egress.SendAsync(Arg.Is<EgressRequest>(request => request.Destination.AbsoluteUri.Contains("calendar", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns(new EgressResponse(200, RSS));
        egress.SendAsync(Arg.Is<EgressRequest>(request => request.Destination.AbsoluteUri.Contains("newsflash", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns<EgressResponse>(_ => throw new EgressRefusedException("host not allowlisted"));
        var store = Substitute.For<IDomainFactStore>();
        var written = new List<DomainFact>();
        store.RecordAsync(Arg.Do<DomainFact>(written.Add), Arg.Any<CancellationToken>()).Returns(true);
        var options = new CivicFeedOptions { FeedDelaySeconds = 0 };
        var service = new CivicFeedCollectorService(
            store, egress, Options.Create(options), new FakeTimeProvider(now), NullLogger<CivicFeedCollectorService>.Instance);

        var result = await service.RunPassAsync(new ProactiveContext(Guid.NewGuid(), now, null), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Completed, result.Status);
        Assert.Equal(2, written.Count);
        Assert.All(written, fact => Assert.Equal(("civic", "meeting", "lakeville-calendar"), (fact.Domain, fact.Category, fact.Source)));
        Assert.Equal(new DateOnly(2026, 8, 26), written[0].AsOf);
        Assert.Equal(new DateOnly(2026, 8, 25), written[1].AsOf);
        Assert.Equal("Finance Committee Meeting — https://www.lakevillemn.gov/calendar.aspx?EID=1", written[0].Description);
    }
}
