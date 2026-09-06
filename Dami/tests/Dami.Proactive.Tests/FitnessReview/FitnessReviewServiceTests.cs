using Dami.Contracts.Domains;
using Dami.Contracts.Proactive;
using Dami.Proactive.FitnessReview;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.FitnessReview;

public sealed class FitnessReviewServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly IFitnessStore store = Substitute.For<IFitnessStore>();

    private static ProactiveContext Context() => new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, null);

    private FitnessReviewService Service() => new(this.store, new FakeTimeProvider(now), NullLogger<FitnessReviewService>.Instance);

    [Fact]
    public async Task A_Week_With_Lifting_Should_Surface_The_Summary_First()
    {
        this.store.SnapshotAsync(Arg.Any<CancellationToken>()).Returns(new FitnessSnapshot([], [
            new FitnessSet(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-3), "leg press", "legs", 1, 10, 300m, 7, false),
            new FitnessSet(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-10), "leg press", "legs", 1, 10, 280m, 7, false)], []));

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        var surfacing = Assert.Single(result.Surfacings);
        Assert.Equal("Your week in the gym", surfacing.Title);
        Assert.StartsWith("This week: 1 lifting session(s)", surfacing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Empty_Log_Should_Stay_Quiet()
    {
        this.store.SnapshotAsync(Arg.Any<CancellationToken>()).Returns(new FitnessSnapshot([], [], []));

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }
}
