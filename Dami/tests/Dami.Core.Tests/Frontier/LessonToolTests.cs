using Dami.Contracts.Memory;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>A correction becomes a standing lesson: recorded once, carried into every turn.</summary>
public sealed class LessonToolTests
{
    private const string STEVE = "No. I'm not private about my workouts, stop disguising them.";

    private readonly IObservationCorpus corpus = Substitute.For<IObservationCorpus>();

    private LessonTool Subject() => new(this.corpus, TimeProvider.System, NullLogger<LessonTool>.Instance);

    [Fact]
    public async Task Should_Record_The_Lesson_With_Its_Provenance()
    {
        var trace = Guid.NewGuid();

        await this.Subject().LearnAsync(
            trace, "discord:1", "Do not disguise Steve's workouts; he is not private about them.",
            "I'm not private about my workouts", STEVE, CancellationToken.None);

        await this.corpus.Received(1).RecordAsync(
            Arg.Is<Observation>(observation =>
                observation.Source == LessonTool.SOURCE
                && observation.Body == "Do not disguise Steve's workouts; he is not private about them."
                && observation.Metadata!["trace"] == trace.ToString("N")
                && observation.Metadata["channel"] == "discord:1"
                && observation.Metadata["quote"] == "I'm not private about my workouts"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Say_It_Will_Carry_The_Lesson()
    {
        var result = await this.Subject().LearnAsync(
            Guid.NewGuid(), "gui", "Be brief about the weather.", "keep the weather short", "please keep the weather short", CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task An_Empty_Lesson_Should_Be_Refused_Before_Anything_Is_Written()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().LearnAsync(Guid.NewGuid(), "gui", " ", "stop", "stop", CancellationToken.None));

        await this.corpus.DidNotReceive().RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Lesson_Whose_Quote_Steve_Did_Not_Say_Should_Be_Refused()
    {
        // Hermes' most-cited failure: it judged its own work and learned from the verdict.
        // A lesson Dami drew for herself is not a correction from Steve.
        var result = await this.Subject().LearnAsync(
            Guid.NewGuid(), "discord:1", "Always add a summary table.", "tables are great", STEVE, CancellationToken.None);

        Assert.False(result.Success);
        await this.corpus.DidNotReceive().RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Lesson_On_A_Turn_Steve_Did_Not_Speak_In_Should_Be_Refused()
    {
        // A scheduled job's request is Dami's own instruction; there is nothing to quote.
        var result = await this.Subject().LearnAsync(
            Guid.NewGuid(), "discord:1", "Be brief.", "be brief", null, CancellationToken.None);

        Assert.False(result.Success);
        await this.corpus.DidNotReceive().RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_Quote_Should_Match_Despite_Case_Punctuation_And_Spacing()
    {
        var result = await this.Subject().LearnAsync(
            Guid.NewGuid(), "discord:1", "Do not disguise his workouts.", "im NOT private  about my workouts",
            STEVE, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task A_Lesson_Longer_Than_One_Sentence_Should_Be_Refused()
    {
        // Twenty lessons ride every turn; one essay would crowd out the rest.
        var result = await this.Subject().LearnAsync(
            Guid.NewGuid(), "discord:1", new string('x', 301), "I'm not private", STEVE, CancellationToken.None);

        Assert.False(result.Success);
        await this.corpus.DidNotReceive().RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }
}
