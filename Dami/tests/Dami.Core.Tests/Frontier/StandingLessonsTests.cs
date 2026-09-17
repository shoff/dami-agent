using Dami.Contracts.Memory;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>What the frontier is told on every turn: the lessons, newest first, never a blocked turn.</summary>
public sealed class StandingLessonsTests
{
    private readonly IObservationCorpus corpus = Substitute.For<IObservationCorpus>();

    [Fact]
    public async Task LinesAsync_Should_Return_The_Recorded_Lessons()
    {
        this.Lessons("Be brief about the weather.", "Never disguise his workouts.");

        var lines = await this.Subject().LinesAsync(CancellationToken.None);

        Assert.Equal(["Be brief about the weather.", "Never disguise his workouts."], lines);
    }

    [Fact]
    public async Task LinesAsync_Should_Drop_A_Repeated_Lesson()
    {
        this.Lessons("Be brief about the weather.", "be brief about the weather.");

        var lines = await this.Subject().LinesAsync(CancellationToken.None);

        Assert.Single(lines);
    }

    [Fact]
    public async Task LinesAsync_Should_Read_Only_The_Lesson_Source()
    {
        this.Lessons();

        await this.Subject().LinesAsync(CancellationToken.None);

        _ = this.corpus.Received(1).FromSourceAsync(LessonTool.SOURCE, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinesAsync_Should_Answer_Empty_Rather_Than_Block_A_Turn_When_The_Corpus_Fails()
    {
        this.corpus.FromSourceAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("database down"));

        var lines = await this.Subject().LinesAsync(CancellationToken.None);

        Assert.Empty(lines);
    }

    private void Lessons(params string[] bodies)
    {
        var observations = new List<Observation>();
        foreach (var body in bodies)
        {
            observations.Add(new Observation(Guid.NewGuid(), DateTimeOffset.UnixEpoch, LessonTool.SOURCE, body));
        }

        this.corpus.FromSourceAsync(LessonTool.SOURCE, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsync(observations));
    }

    private StandingLessons Subject() => new(this.corpus, NullLogger<StandingLessons>.Instance);

    private static async IAsyncEnumerable<Observation> AsAsync(List<Observation> observations)
    {
        foreach (var observation in observations)
        {
            yield return observation;
        }

        await Task.CompletedTask;
    }
}
