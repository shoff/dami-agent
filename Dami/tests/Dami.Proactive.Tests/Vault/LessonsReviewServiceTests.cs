using Dami.Contracts.Memory;
using Dami.Contracts.Proactive;
using Dami.Proactive.Vault;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Vault;

/// <summary>Lessons about to lapse, said before they do, with the words to say to keep them (B11).</summary>
public sealed class LessonsReviewServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 12, 20, 15, 0, 0, TimeSpan.Zero);

    private static async IAsyncEnumerable<Observation> ManyAsync(params Observation[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private static Observation Lesson(string body, int daysAgo) => new(Guid.NewGuid(), now.AddDays(-daysAgo), "lesson", body);

    [Fact]
    public async Task Only_Lessons_Lapsing_Within_Two_Weeks_Should_Be_Named_Once_Each()
    {
        var corpus = Substitute.For<IObservationCorpus>();
        corpus.FromSourceAsync("lesson", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ManyAsync(
            Lesson("Keep weather to one line.", 80),
            Lesson("keep weather to one line.", 85),
            Lesson("Be brief about the gym.", 20),
            Lesson("Stop apologising.", 95)));

        var result = await new LessonsReviewService(corpus, new FakeTimeProvider(now))
            .RunPassAsync(new ProactiveContext(Guid.NewGuid(), now, null), CancellationToken.None);

        var note = Assert.Single(result.Surfacings);
        Assert.Contains("Keep weather to one line. (lapses Dec 30)", note.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("gym", note.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("apologising", note.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_Lapsing_Soon_Should_Say_Nothing()
    {
        var corpus = Substitute.For<IObservationCorpus>();
        corpus.FromSourceAsync("lesson", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ManyAsync(Lesson("Be brief.", 3)));

        var result = await new LessonsReviewService(corpus, new FakeTimeProvider(now))
            .RunPassAsync(new ProactiveContext(Guid.NewGuid(), now, null), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }
}
