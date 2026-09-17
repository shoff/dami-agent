using Dami.Contracts.Memory;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>A correction becomes a standing lesson: recorded once, carried into every turn.</summary>
public sealed class LessonToolTests
{
    private readonly IObservationCorpus corpus = Substitute.For<IObservationCorpus>();

    private LessonTool Subject() => new(this.corpus, TimeProvider.System, NullLogger<LessonTool>.Instance);

    [Fact]
    public async Task Should_Record_The_Lesson_With_Its_Provenance()
    {
        var trace = Guid.NewGuid();

        await this.Subject().LearnAsync(
            trace, "discord:1", "Do not disguise Steve's workouts; he is not private about them.", CancellationToken.None);

        await this.corpus.Received(1).RecordAsync(
            Arg.Is<Observation>(observation =>
                observation.Source == LessonTool.SOURCE
                && observation.Body == "Do not disguise Steve's workouts; he is not private about them."
                && observation.Metadata!["trace"] == trace.ToString("N")
                && observation.Metadata["channel"] == "discord:1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Say_It_Will_Carry_The_Lesson()
    {
        var result = await this.Subject().LearnAsync(Guid.NewGuid(), "gui", "Be brief about the weather.", CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task An_Empty_Lesson_Should_Be_Refused_Before_Anything_Is_Written()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().LearnAsync(Guid.NewGuid(), "gui", " ", CancellationToken.None));

        await this.corpus.DidNotReceive().RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }
}
