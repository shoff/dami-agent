using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Dami.Proactive.Civic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Civic;

public sealed class CivicAgendaServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 25, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunPassAsync_Should_Surface_The_Coming_Weeks_Meetings_Once()
    {
        var store = Substitute.For<IDomainFactStore>();
        store.BetweenAsync("civic", new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 1), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(
                new DomainFact(Guid.NewGuid(), "civic", new DateOnly(2026, 8, 26), "meeting", "Finance Committee Meeting — https://x/1", "lakeville-calendar", now),
                new DomainFact(Guid.NewGuid(), "civic", new DateOnly(2026, 8, 26), "notice", "Family Flicks — https://x/2", "lakeville-news", now),
                new DomainFact(Guid.NewGuid(), "civic", new DateOnly(2026, 8, 31), "meeting", "City Council Meeting — https://x/3", "lakeville-calendar", now)));
        var queue = Substitute.For<ISurfacingQueue>();
        queue.RecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(SurfacingsAsync());
        var service = new CivicAgendaService(store, queue, new FakeTimeProvider(now), NullLogger<CivicAgendaService>.Instance);

        var first = await service.RunPassAsync(new ProactiveContext(Guid.NewGuid(), now, null), CancellationToken.None);
        queue.RecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(SurfacingsAsync(first.Surfacings[0]));
        var second = await service.RunPassAsync(new ProactiveContext(Guid.NewGuid(), now, now), CancellationToken.None);

        var surfacing = Assert.Single(first.Surfacings);
        Assert.Equal("Civic calendar, week of 2026-08-25: 2 meeting(s)", surfacing.Title);
        Assert.Contains("Wed 2026-08-26  Finance Committee Meeting", surfacing.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Family Flicks", surfacing.Body, StringComparison.Ordinal);
        Assert.Empty(second.Surfacings);
    }

    private static async IAsyncEnumerable<DomainFact> FactsAsync(params DomainFact[] facts)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var fact in facts)
        {
            yield return fact;
        }
    }

    private static async IAsyncEnumerable<Surfacing> SurfacingsAsync(params Surfacing[] surfacings)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var surfacing in surfacings)
        {
            yield return surfacing;
        }
    }
}
