using System.Text.Json;
using Dami.Contracts.Domains;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>"4x12 140 lbs RPE 7" plus a photo becomes rows in the exercise log.</summary>
public sealed class FitnessToolsTests
{
    private readonly IFitnessStore store = Substitute.For<IFitnessStore>();

    private FitnessTools Subject() => new(this.store, TimeProvider.System, NullLogger<FitnessTools>.Instance);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Log_Sets_Should_Expand_Sets_By_Reps_Into_One_Resistance_Entry()
    {
        this.store.RecordResistanceAsync(Arg.Any<FitnessResistanceEntry>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        var result = await this.Subject().LogSetsAsync(
            Args("""{"exercise":"biceps curl (Hammer Strength)","sets":4,"reps":12,"weightLbs":140,"rpe":7,"equipment":"Machine"}"""),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Logged 4x12 biceps curl (Hammer Strength) at 140 lb, RPE 7.", result.Text);
        await this.store.Received(1).RecordResistanceAsync(
            Arg.Is<FitnessResistanceEntry>(entry =>
                entry.Exercise == "biceps curl (Hammer Strength)"
                && entry.Sets.Count == 4
                && entry.Sets.All(set => set.Reps == 12 && set.WeightLbs == 140 && set.Rpe == 7)
                && entry.Equipment == "machine"
                && entry.Source == "claude_chat"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Log_Sets_Should_Refuse_Nonsense_Before_Writing()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().LogSetsAsync(Args("""{"exercise":"x","sets":0,"reps":12}"""), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().LogSetsAsync(Args("""{"sets":3,"reps":12}"""), CancellationToken.None));

        await this.store.DidNotReceive().RecordResistanceAsync(Arg.Any<FitnessResistanceEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Log_Cardio_Should_Convert_Minutes_And_Keep_The_Display_Numbers()
    {
        this.store.RecordCardioAsync(Arg.Any<FitnessCardioEntry>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        var result = await this.Subject().LogCardioAsync(
            Args("""{"modality":"Treadmill","minutes":30.5,"distanceMi":2.1,"calories":310,"inclinePct":3,"hrAvg":138}"""),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Logged treadmill for 30.5 min, 2.1 mi.", result.Text);
        await this.store.Received(1).RecordCardioAsync(
            Arg.Is<FitnessCardioEntry>(entry =>
                entry.Modality == "treadmill" && entry.DurationSeconds == 1830 && entry.Calories == 310
                && entry.InclinePct == 3 && entry.HrAvg == 138),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Log_Cardio_Should_Name_The_Allowed_Modalities_When_Given_An_Unknown_One()
    {
        var result = await this.Subject().LogCardioAsync(Args("""{"modality":"stairmaster"}"""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("elliptical", result.Text, StringComparison.Ordinal);
        await this.store.DidNotReceive().RecordCardioAsync(Arg.Any<FitnessCardioEntry>(), Arg.Any<CancellationToken>());
    }
}
